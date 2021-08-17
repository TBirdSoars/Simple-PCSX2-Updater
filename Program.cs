using HtmlAgilityPack;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace Simple_PCSX2_Updater
{
    internal class Program
    {
        private static readonly HttpClient client = new HttpClient();
        private static readonly Uri baseURL = new Uri(@"https://buildbot.orphis.net");
        private static readonly string urlParam = @"/pcsx2/index.php";
        private static readonly string zipFile = @"pcsx2.7z";
        private static string currentDir = "";
        private static string pcsx2FullDir = "";
        private static string zipFullDir = "";

        private static async Task Main()
        {
            // Begin
            Console.WriteLine("Simple PCSX2 Updater - By TBirdSoars");
            Console.WriteLine("```");

            // Identify where this app is and check for pcsx2.exe
            Console.WriteLine("Finding PCSX2... ");
            ConsoleKey response = ConsoleKey.N;
            try
            {
                //currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                currentDir = Path.GetDirectoryName(AppContext.BaseDirectory);
                pcsx2FullDir = Path.Combine(currentDir, "pcsx2.exe");
                if (!File.Exists(pcsx2FullDir))
                {
                    // Wrong folder or first time downloading
                    do
                    {
                        Console.Write("pcsx2.exe not found in the current folder. Do you want to download PCSX2 to this folder anyway? (y/n) ");
                        response = Console.ReadKey(false).Key;
                        Console.WriteLine("");
                    }
                    while (response != ConsoleKey.Y && response != ConsoleKey.N);
                }
                else
                {
                    response = ConsoleKey.Y;
                    Console.WriteLine("PCSX2 Found!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                response = ConsoleKey.N;
            }

            // Set full path of zip file
            zipFullDir = Path.Combine(currentDir, zipFile);

            // Proceed?
            if (response == ConsoleKey.Y)
            {
                // Download webpage
                Console.WriteLine("Downloading PCSX2... ");
                HtmlDocument htmlDoc = new HtmlDocument();
                HtmlWeb htmlWeb = new HtmlWeb();
                try
                {
                    htmlDoc = await htmlWeb.LoadFromWebAsync(baseURL + urlParam);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

                //
                // TODO - Account for nulls if requests fail
                //
                // Get table with all recent releases
                HtmlNode tableNode = htmlDoc.DocumentNode.SelectSingleNode("//table[@class='listing']");
                // OK - get items of table, skip first item of table, only get items that contain more than one element, get the table as a list of lists of htmlnodes?!
                List<List<HtmlNode>> tableListList = tableNode.Descendants("tr").Skip(1).Where(tr => tr.Elements("td").Count() > 1).Select(tr => tr.Elements("td").ToList()).ToList();

                // Convert list of lists to datatable
                DataTable releaseTable = new DataTable();
                releaseTable.Columns.Add("commit", typeof(string));
                releaseTable.Columns.Add("username", typeof(string));
                releaseTable.Columns.Add("date", typeof(string));
                releaseTable.Columns.Add("build", typeof(string));
                releaseTable.Columns.Add("change", typeof(string));
                foreach (List<HtmlNode> nodeList in tableListList)
                {
                    DataRow row = releaseTable.NewRow();

                    for (int i = 0; i < nodeList.Count; i++)
                    {
                        // The attributes are on the child nodes... I swear to fucking god I will never touch HTML again
                        // Check if there is an href attribute, if not just grab innerText or nothing
                        if (nodeList[i].HasChildNodes)
                        {
                            // Get attribute from childnode
                            if (nodeList[i].FirstChild.Attributes["href"] != null)
                            {
                                row[i] = nodeList[i].FirstChild.Attributes["href"].Value;
                            }
                            else
                            {
                                row[i] = nodeList[i].FirstChild.InnerText;
                            }
                        }
                        else
                        {
                            // No child nodes, just get outerHTML
                            row[i] = nodeList[i].OuterHtml;
                        }
                    }

                    releaseTable.Rows.Add(row);
                }

                // Remove "No build" entries
                DataTable buildTable = releaseTable.Select("build <> 'No build'").CopyToDataTable();

                // Convert the date column to datetime, then sort to find newest
                DataTable finalTable = buildTable.Clone();
                finalTable.Columns["date"].DataType = typeof(DateTime);
                foreach (DataRow row in buildTable.Rows)
                {
                    finalTable.ImportRow(row);
                }
                finalTable.DefaultView.Sort = "date DESC";
                finalTable = finalTable.DefaultView.ToTable();

                string build_Path_and_Query = finalTable.Rows[0]["build"].ToString().Replace("amp;", "");
                Uri downloadURL = new Uri(baseURL + build_Path_and_Query);


                // Get download from URL, from finalTable
                await DownloadArchive(downloadURL, zipFullDir);


                // Get name of extract folder
                string folderName = "pcsx2-" + HttpUtility.ParseQueryString(downloadURL.Query).Get("rev");
                folderName += "-" + HttpUtility.ParseQueryString(downloadURL.Query).Get("platform");
                string extractFolder = Path.Combine(currentDir, folderName);

                // Extract 7zip archive
                Console.WriteLine("Extracting PCSX2... ");
                ExtractArchive(zipFullDir);


                // Move files into pcsx2.exe directory
                Console.WriteLine("Moving files...");
                MoveAll(extractFolder, currentDir);


                // Cleanup
                Console.WriteLine($"Cleaning up...");
                // Delete 7z file
                Cleanup(zipFullDir);
                // Delete extract folder
                Cleanup(extractFolder);


                // Done!
                Console.WriteLine("Done!");
            }


            Console.WriteLine("```");


            // End execution
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        private static DataTable GetBuildTable()
        {
            DataTable output = new DataTable();

            return output;
        }

        private async static Task DownloadArchive(Uri uri, string dest)
        {
            try
            {
                using (HttpResponseMessage httpResponseMessage = await client.GetAsync(uri))
                using (Stream stream = await httpResponseMessage.Content.ReadAsStreamAsync())
                {
                    FileInfo fileInfo = new FileInfo(dest);

                    using (FileStream fileStream = fileInfo.OpenWrite())
                    {
                        stream.CopyTo(fileStream);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void ExtractArchive(string src)
        {
            try
            {
                if (File.Exists(src))
                {
                    using (SevenZipArchive sevenZipArchive = SevenZipArchive.Open(src))
                    using (IReader reader = sevenZipArchive.ExtractAllEntries())
                    {
                        while (reader.MoveToNextEntry())
                        {
                            ExtractionOptions extractionOptions = new ExtractionOptions
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            };

                            reader.WriteEntryToDirectory(currentDir, extractionOptions);
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Download file '{zipFile}' not found in current directory.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void MoveAll(string src, string dest)
        {
            //
            // https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/file-system/how-to-copy-delete-and-move-files-and-folders
            //
            try
            {
                if (Directory.Exists(src) && Directory.Exists(dest))
                {
                    string[] files = Directory.GetFiles(src);
                    string[] folders = Directory.GetDirectories(src);

                    // Copy the files and overwrite destination files if they already exist.
                    foreach (string file in files)
                    {
                        // Use static Path methods to extract only the file name from the path.
                        string fileName = Path.GetFileName(file);
                        string destFile = Path.Combine(dest, fileName);
                        File.Move(file, destFile, true);
                    }

                    // Copy the folders
                    foreach (string folder in folders)
                    {
                        // Use static Path methods to extract only the folder name from the path.
                        string folderName = Path.GetFileName(folder);
                        string destFolder = Path.Combine(dest, folderName);
                        Directory.Move(folder, destFolder);
                    }
                }
                else
                {
                    Console.WriteLine($"{src} and {dest} do not exist!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void Cleanup(string src)
        {
            try
            {
                if (File.Exists(src))
                {
                    File.Delete(src);
                }
                else if (Directory.Exists(src))
                {
                    Directory.Delete(src);
                }
                else
                {
                    Console.WriteLine($"{src} does not exist!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
