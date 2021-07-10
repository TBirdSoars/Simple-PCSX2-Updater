using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Simple_PCSX2_Updater
{
    class Program
    {
        static readonly String baseURL = @"https://buildbot.orphis.net";
        static readonly String ext = @"/pcsx2/index.php";
        static private String downloadURL = "";
        static private String currentDir = "";
        static private String pcsx2EXE = "";
        static private ConsoleKey response = ConsoleKey.N;
        static private HtmlWeb web = new HtmlWeb();
        static private HtmlDocument htmlDoc;
        static private HtmlNode tableNode;
        static private List<List<HtmlNode>> tableListList;
        static private DataTable releaseTable = new DataTable();
        static private DataTable buildTable = new DataTable();
        static private DataTable finalTable = new DataTable();

        static async Task Main(string[] args)
        {
            // Begin
            Console.WriteLine("Simple PCSX2 Updater - By TBirdSoars");
            Console.WriteLine("```");

            // Identify where this app is and check for pcsx2.exe
            Console.WriteLine("Finding PCSX2... ");
            try
            {
                currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                pcsx2EXE = Path.Combine(currentDir, "pcsx2.exe");
                if (!File.Exists(pcsx2EXE))
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
                    Console.WriteLine("PCSX2 Found!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                response = ConsoleKey.N;
            }

            // Proceed?
            if (response == ConsoleKey.Y)
            {
                // Download
                Console.WriteLine("Downloading PCSX2... ");
                try
                {
                    htmlDoc = web.Load(baseURL + ext);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
                // Get table with all recent releases
                tableNode = htmlDoc.DocumentNode.SelectSingleNode("//table[@class='listing']");
                // OK - get items of table, skip first item of table, only get items that contain more than one element, get the table as a list of lists of htmlnodes?!
                tableListList = tableNode.Descendants("tr").Skip(1).Where(tr => tr.Elements("td").Count() > 1).Select(tr => tr.Elements("td").ToList()).ToList();

                // Convert list of lists to datatable
                releaseTable.Columns.Add("commit",typeof(String));
                releaseTable.Columns.Add("username",typeof(String));
                releaseTable.Columns.Add("date",typeof(String));
                releaseTable.Columns.Add("build",typeof(String));
                releaseTable.Columns.Add("change",typeof(String));
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
                            if(nodeList[i].FirstChild.Attributes["href"] != null)
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
                buildTable = releaseTable.Select("build <> 'No build'").CopyToDataTable();

                // Convert the date column to datetime, then sort to find newest
                finalTable = buildTable.Clone();
                finalTable.Columns["date"].DataType = typeof(DateTime);
                foreach (DataRow row in buildTable.Rows)
                {
                    finalTable.ImportRow(row);
                }
                finalTable.DefaultView.Sort = "date DESC";
                finalTable = finalTable.DefaultView.ToTable();

                // Get download from URL, from finalTable
                using (HttpClient client = new HttpClient())
                {
                    try
                    {
                        downloadURL = baseURL + finalTable.Rows[0]["build"].ToString().Replace("amp;", "");
                        HttpResponseMessage httpResponseMessage = await client.GetAsync(downloadURL);

                        using (Stream stream = await httpResponseMessage.Content.ReadAsStreamAsync())
                        {
                            FileInfo fileInfo = new FileInfo("pcsx2.7z");
                            using (FileStream fileStream = fileInfo.OpenWrite())
                            {
                                await stream.CopyToAsync(fileStream);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }

                // Extract 7zip archive
                Console.WriteLine("Extracting PCSX2... ");


                // Overwrite 7zip with download
                Console.WriteLine("Overwriting existing PCSX2... ");


                // Done!
                Console.WriteLine("Done!");
            }

            Console.WriteLine("```");

            // End execution
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }
    }
}
