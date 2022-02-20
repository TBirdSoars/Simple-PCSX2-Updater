using Newtonsoft.Json;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Simple_PCSX2_Updater
{
    internal class Program
    {
        private static readonly HttpClient client = new HttpClient();

        private static async Task Main()
        {
            Uri baseDownURI = new Uri(@"https://github.com/PCSX2/pcsx2/releases/download/");
            ConsoleKey response = ConsoleKey.N;

            string currentDir = string.Empty;
            string pcsx2FullDir = string.Empty;
            string version = string.Empty;
            string bit = string.Empty;
            string zipFile = string.Empty;
            string folderName = string.Empty;
            string zipFullDir = string.Empty;
            string extractFullDir = string.Empty;

            // Begin
            Console.WriteLine("Simple PCSX2 Updater - By TBirdSoars");
            Console.WriteLine("```");

            // Identify where this app is and check for pcsx2.exe
            Console.WriteLine("Finding PCSX2... ");
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
                        Console.WriteLine();
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
                Console.WriteLine($"Exception Finding PCSX2: {ex.Message}");
            }

            // Proceed?
            if (response == ConsoleKey.Y)
            {
                // Get build version
                Console.WriteLine("Getting build version... ");
                version = await GetNightlyVersion();
                if (version == "")
                {
                    Console.WriteLine("Failed to get version, exiting...");
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey();
                    return;
                }


                // Set names of folders and archives
                if (Environment.Is64BitOperatingSystem)
                {
                    bit = "64bit";
                }
                else
                {
                    bit = "32bit";
                }
                zipFile = $"pcsx2-{version}-windows-{bit}-AVX2.7z";
                folderName = Path.GetFileNameWithoutExtension(zipFile);
                zipFullDir = Path.Combine(currentDir, zipFile);
                extractFullDir = Path.Combine(currentDir, folderName);


                // Set download URI
                Console.WriteLine("Building download url... ");
                UriBuilder builder = new UriBuilder(baseDownURI);
                builder.Path += version;
                builder.Path += "/";
                builder.Path += zipFile;


                // Get download from URL
                Console.WriteLine($"Downloading version {folderName}... ");
                await DownloadFile(builder.Uri, currentDir);
                if (!File.Exists(zipFullDir))
                {
                    Console.WriteLine("Failed to download, exiting...");
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey();
                    return;
                }


                // Extract 7zip archive
                Console.WriteLine("Extracting PCSX2... ");
                ExtractArchive(zipFullDir);


                // Done!
                Console.WriteLine("Done!");
            }

            // End execution
            Console.WriteLine("```");
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        // Gets the latest nightly build version
        private static async Task<string> GetNightlyVersion()
        {
            string version = string.Empty;
            Uri releasesJSON = new Uri(@"https://api.pcsx2.net/v1/latestReleasesAndPullRequests");

            try
            {
                using (HttpResponseMessage response = await client.GetAsync(releasesJSON))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("DownloadArchive Error: Did not receive 200 OK status code.");
                        return version;
                    }

                    if (response.Content == null)
                    {
                        Console.WriteLine("DownloadArchive Error: Response message content was null.");
                        return version;
                    }

                    // Parse the JSON object
                    using (Stream stream = await response.Content.ReadAsStreamAsync())
                    using (StreamReader sReader = new StreamReader(stream))
                    using (JsonTextReader jReader = new JsonTextReader(sReader))
                    {
                        bool done = false;
                        while (jReader.Read() && !done)
                        {
                            // Skip stable list
                            if (Convert.ToString(jReader.Value).Equals("stableReleases"))
                            {
                                jReader.Skip();
                            }

                            if (Convert.ToString(jReader.Value).Equals("nightlyReleases"))
                            {
                                // Read until version is found
                                while (jReader.Read() && !done)
                                {
                                    if (Convert.ToString(jReader.Value).Equals("version"))
                                    {
                                        version = jReader.ReadAsString();
                                        done = true;
                                    }
                                }
                            }

                            // Skip stable list
                            if (Convert.ToString(jReader.Value).Equals("pullRequestBuilds"))
                            {
                                jReader.Skip();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetLatestNightly Exception: {ex.Message}");
            }

            return version;
        }

        // Downloads file from Uri into destination path
        private static async Task DownloadFile(Uri fileURI, string destPath)
        {
            // Set output file
            string destFile = Path.Combine(destPath, Path.GetFileName(fileURI.LocalPath));

            try
            {
                using (HttpResponseMessage message = await client.GetAsync(fileURI))
                {
                    if (!message.IsSuccessStatusCode)
                    {
                        Console.WriteLine("DownloadArchive Error: Did not receive 200 OK status code.");
                        return;
                    }

                    if (message.Content == null)
                    {
                        Console.WriteLine("DownloadArchive Error: Response message content was null.");
                        return;
                    }

                    using (Stream stream = await message.Content.ReadAsStreamAsync())
                    using (FileStream fileStream = new FileInfo(destFile).OpenWrite())
                    {
                        stream.CopyTo(fileStream);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DownloadArchive Exception: {ex.Message}");
            }
        }

        // Extracts 7z archive, then deletes archive
        private static void ExtractArchive(string src)
        {
            string a = Path.GetDirectoryName(src);

            try
            {
                if (File.Exists(src))
                {
                    using (SevenZipArchive sevenZipArchive = SevenZipArchive.Open(src))
                    using (IReader reader = sevenZipArchive.ExtractAllEntries())
                    {
                        //reader.WriteAllToDirectory(dest, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                        reader.WriteAllToDirectory(Path.GetDirectoryName(src), new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                    }

                    // Cleanup
                    File.Delete(src);
                }
                else
                {
                    Console.WriteLine($"ExtractArchive Error: '{src}' does not exist.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ExtractArchive Exception: {ex.Message}");
            }
        }
    }
}
