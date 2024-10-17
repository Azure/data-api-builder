// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Cryptography;

namespace Azure.DataApiBuilder.Config.Utilities;

internal class FileUtilities
{
    /// <summary>
    /// Computes a SHA1 hash of the file at the given path.
    /// Includes an exponential back-off retry mechanism to accommodate
    /// circumstances where the file may be in use by another process.
    /// "SHA-1 (Secure Hash Algorithm 1) is a hash function which takes an input
    /// and produces a 160-bit (20-byte) hash value known as a message digest
    /// – typically rendered as 40 hexadecimal digits."
    /// This utility function code is based on the example provided in the
    /// .NET ChangeToken documentation.
    /// </summary>
    /// <param name="filePath">Abosolute file path or relative file path.</param>
    /// <returns>20 byte message digest.</returns>
    /// <seealso cref="https://en.wikipedia.org/wiki/SHA-1"/>
    /// <seealso cref="https://learn.microsoft.com/en-us/aspnet/core/fundamentals/change-tokens?view=aspnetcore-8.0#:~:text=exponential%20back%2Doff.-,Utilities/Utilities.cs%3A,-C%23"/>
    /// <exception cref="FileNotFoundException">https://learn.microsoft.com/en-us/dotnet/api/system.io.file.openread?view=net-8.0#exceptions</exception>
    public static byte[] ComputeHash(string filePath)
    {
        // Exponential back-off retry mechanism.
        int runCount = 1;

        // Arbitrary limit of 4 retries.
        while (runCount < 4)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    // There are a number of reasons why "OpenRead" could fail with IOException.
                    // DirectoryNotFound, EndOfStream, FileNotFound, FileLoad, PathTooLong, etc.
                    // https://learn.microsoft.com/en-us/dotnet/api/system.io.file.openread?view=net-8.0#exceptions
                    // https://learn.microsoft.com/en-us/dotnet/api/system.io.ioexception?view=net-8.0#remarks
                    using (FileStream fs = File.OpenRead(filePath))
                    {
                        return SHA1.Create().ComputeHash(fs);
                    }
                }
                else
                {
                    Console.WriteLine($"Path '{filePath}' not found in: " + Directory.GetCurrentDirectory());
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

        #if NET8_0_OR_GREATER
        return new byte[SHA1.HashSizeInBytes];
        #else
        return new byte[20];
        #endif
    }
}
