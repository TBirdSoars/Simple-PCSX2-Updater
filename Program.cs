using HtmlAgilityPack;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
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
            }

            // Set full path of zip file
            zipFullDir = Path.Combine(currentDir, zipFile);

            // Proceed?
            if (response == ConsoleKey.Y)
            {
                // Get build list
                Console.WriteLine("Getting build list... ");
                DataTable buildTable = await GetBuildTable();
                if (buildTable.Rows.Count == 0 || !buildTable.Columns.Contains("build"))
                {
                    Console.WriteLine("Failed to get list of builds, exiting...");
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey();
                    return;
                }


                // Get download URL
                Console.WriteLine("Building download url... ");
                string build_Path_and_Query = buildTable.Rows[0]["build"].ToString().Replace("amp;", "");
                Uri downloadURL = new Uri(baseURL, build_Path_and_Query);
                if (HttpUtility.ParseQueryString(downloadURL.Query).Get("rev") == null
                    || HttpUtility.ParseQueryString(downloadURL.Query).Get("platform") == null)
                {
                    Console.WriteLine("Failed to get download URL data, exiting...");
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey();
                    return;
                }


                // Get name of extract folder
                string folderName = "pcsx2-" + HttpUtility.ParseQueryString(downloadURL.Query).Get("rev");
                folderName += "-" + HttpUtility.ParseQueryString(downloadURL.Query).Get("platform");
                string extractFolder = Path.Combine(currentDir, folderName);


                // Get download from URL
                Console.WriteLine($"Downloading version {folderName}... ");
                await DownloadArchive(downloadURL, zipFullDir);


                // Extract 7zip archive
                Console.WriteLine("Extracting PCSX2... ");
                ExtractArchive(zipFullDir);


                // Move files into pcsx2.exe directory
                Console.WriteLine("Moving files...");
                MoveAll(extractFolder, currentDir);


                // Done!
                Console.WriteLine("Done!");
            }

            // End execution
            Console.WriteLine("```");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        private static async Task<DataTable> GetBuildTable()
        {
            DataTable output = new DataTable();

            // Download webpage
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

            // Get table with all recent releases
            HtmlNode tableNode = htmlDoc.DocumentNode.SelectSingleNode("//table[@class='listing']");
            // First make sure stuff exists
            if (tableNode != null)
            {
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
                        if (nodeList[i].HasChildNodes)
                        {
                            // Check if there is an href attribute, if not just grab innerText or nothing
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

                // Remove "No build" entries -  we want only downloads
                releaseTable = releaseTable.Select("build <> 'No build'").CopyToDataTable();

                // Convert the date column to datetime, then sort to find newest
                DataTable finalTable = releaseTable.Clone();
                finalTable.Columns["date"].DataType = typeof(DateTime);
                foreach (DataRow row in releaseTable.Rows)
                {
                    finalTable.ImportRow(row);
                }
                finalTable.DefaultView.Sort = "date DESC";
                output = finalTable.DefaultView.ToTable();
            }
            else
            {
                Console.WriteLine($"{baseURL} returned null");
            }

            return output;
        }

        // Downloads file from Uri as file specified in destination path
        private static async Task DownloadArchive(Uri uri, string dest)
        {
            try
            {
                using (HttpResponseMessage httpResponseMessage = await client.GetAsync(uri))
                {
                    if (httpResponseMessage.StatusCode != HttpStatusCode.OK)
                    {
                        Console.WriteLine("Downlaod Error: Did not receive 200 OK status code.");
                        return;
                    }

                    if (httpResponseMessage.Content == null)
                    {
                        Console.WriteLine("Downlaod Error: Response message content was null.");
                        return;
                    }

                    using (Stream stream = await httpResponseMessage.Content.ReadAsStreamAsync())
                    {
                        FileInfo fileInfo = new FileInfo(dest);

                        using (FileStream fileStream = fileInfo.OpenWrite())
                        {
                            stream.CopyTo(fileStream);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        // Extracts 7z archive, then deletes archive
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

                    // Cleanup
                    File.Delete(src);
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

        // Moves everything from within source folder to destination folder,
        // then deletes source folder
        private static void MoveAll(string src, string dest)
        {
            try
            {
                if (Directory.Exists(src) && Directory.Exists(dest))
                {
                    string[] files = Directory.GetFiles(src);
                    string[] folders = Directory.GetDirectories(src);

                    // Move the files and overwrite destination files if they already exist.
                    foreach (string file in files)
                    {
                        string oldFile = Path.Combine(dest, Path.GetFileName(file));

                        // Delete all files with same name
                        if (File.Exists(oldFile))
                        {
                            File.Delete(oldFile);
                        }

                        // Use static Path methods to extract only the file name from the path.
                        string fileName = Path.GetFileName(file);
                        string destFile = Path.Combine(dest, fileName);
                        File.Move(file, destFile, true);
                    }

                    // Move the folders
                    foreach (string folder in folders)
                    {
                        string oldFolder = Path.Combine(dest, Path.GetFileName(folder));

                        // Delete all folders with same name, starting with contents
                        if (Directory.Exists(oldFolder))
                        {
                            // Delete contents of old folder
                            DirectoryInfo di = new DirectoryInfo(oldFolder);
                            foreach (FileInfo file in di.GetFiles())
                            {
                                file.Delete();
                            }
                            foreach (DirectoryInfo dir in di.GetDirectories())
                            {
                                dir.Delete(true);
                            }

                            // Delete old folder
                            Directory.Delete(oldFolder);
                        }

                        // Use static Path methods to extract only the folder name from the path.
                        string folderName = Path.GetFileName(folder);
                        string destFolder = Path.Combine(dest, folderName);
                        Directory.Move(folder, destFolder);
                    }

                    // Delete src folder
                    Directory.Delete(src);
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
    }
}
