// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.DataApiBuilder.Config.Utilities
{
    internal class FileUtilities
    {
        public static byte[] ComputeHash(string filePath)
        {
            int runCount = 1;

            while (runCount < 4)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        using (FileStream fs = File.OpenRead(filePath))
                        {
                            return System.Security.Cryptography.SHA1
                                .Create().ComputeHash(fs);
                        }
                    }
                    else
                    {
                        throw new FileNotFoundException();
                    }
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"IO Exception, retrying due to {ex.Message}");
                    if (runCount == 3)
                    {
                        throw;
                    }

                    Thread.Sleep(TimeSpan.FromSeconds(Math.Pow(2, runCount)));
                    runCount++;
                }
            }

            return new byte[20];
        }
    }
}
