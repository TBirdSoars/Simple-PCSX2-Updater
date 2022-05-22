using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
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
                bool pcsx2Found = false;
                currentDir = Path.GetDirectoryName(AppContext.BaseDirectory);
                foreach (string dir in Directory.GetFiles(currentDir))
                {
                    string file = Path.GetFileName(dir);
                    if (file.StartsWith("pcsx2") && file.EndsWith(".exe"))
                    {
                        pcsx2Found = true;
                    }
                }
                if (!pcsx2Found)
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
                zipFile = $"pcsx2-{version}-windows-{bit}-AVX2-wxWidgets.7z";
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
                ExtractHere(zipFullDir);


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
                        Console.WriteLine("GetNightlyVersion Error: Did not receive 200 OK status code.");
                        return version;
                    }

                    if (response.Content == null)
                    {
                        Console.WriteLine("GetNightlyVersion Error: Response message content was null.");
                        return version;
                    }

                    // Parse the JSON document
                    using (Stream stream = response.Content.ReadAsStream())
                    using (JsonDocument jDoc = JsonDocument.Parse(stream))
                    using (JsonElement.ObjectEnumerator jOEnum = jDoc.RootElement.EnumerateObject())
                    {
                        // nightlyReleases object
                        if (!jDoc.RootElement.TryGetProperty("nightlyReleases", out JsonElement nightlyReleases))
                        {
                            Console.WriteLine($"GetLatestNightly Error: nightlyReleases not found.");
                            return version;
                        }

                        // data array
                        if (!nightlyReleases.TryGetProperty("data", out JsonElement data))
                        {
                            Console.WriteLine($"GetLatestNightly Error: data not found.");
                            return version;
                        }

                        // First item in array is recent version
                        using (JsonElement.ArrayEnumerator dataEnum = data.EnumerateArray())
                        {
                            // Enter array
                            if (!dataEnum.MoveNext())
                            {
                                Console.WriteLine($"GetLatestNightly Error: no entries in data array.");
                                return version;
                            }

                            // version element string
                            if (!dataEnum.Current.TryGetProperty("version", out JsonElement versionElement))
                            {
                                Console.WriteLine($"GetLatestNightly Error: version not found.");
                                return version;
                            }

                            version = versionElement.GetString();
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

                    using (Stream stream = message.Content.ReadAsStream())
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
        private static void ExtractHere(string src)
        {
            try
            {
                if (File.Exists(src))
                {
                    using (SevenZipArchive sevenZipArchive = SevenZipArchive.Open(src))
                    using (IReader reader = sevenZipArchive.ExtractAllEntries())
                    {
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
