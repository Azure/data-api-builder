// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using System.Security.Cryptography;

namespace Azure.DataApiBuilder.Config.Utilities;

internal class FileUtilities
{
    /// <summary>
    /// Limit the number of retries when failures occur.
    /// </summary>
    public static readonly int RunLimit = 3;

    /// <summary>
    /// Base of the exponential retry back-off.
    /// -> base ^ runCount
    /// e.g. 2^1 , 2^2, 2^3, etc.
    /// </summary>
    public static readonly int ExponentialRetryBase = 2;

    /// <summary>
    /// Computes the SHA256 hash for the input data (file contents) at the given path.
    /// Includes an exponential back-off retry mechanism to accommodate
    /// circumstances where the file may be in use by another process.
    /// "The hash is used as a unique value of fixed size representing a large amount of data.
    /// Hashes of two sets of data should match if and only if the corresponding data also matches.
    /// Small changes to the data result in large unpredictable changes in the hash."
    /// This utility function code is based on and modified from the example provided in the
    /// .NET ChangeToken documentation.
    /// </summary>
    /// <param name="fileSystem">File system abstraction.</param>
    /// <param name="filePath">Abosolute file path or relative file path.</param>
    /// <returns>32 byte message digest.</returns>
    /// <seealso cref="https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256#remarks"/>
    /// <seealso cref="https://learn.microsoft.com/en-us/aspnet/core/fundamentals/change-tokens#:~:text=exponential%20back%2Doff.-,Utilities/Utilities.cs%3A,-C%23"/>
    /// <exception cref="FileNotFoundException">https://learn.microsoft.com/en-us/dotnet/api/system.io.file.openread#exceptions</exception>
    public static byte[] ComputeHash(IFileSystem fileSystem, string filePath)
    {
        // Exponential back-off retry mechanism.
        int runCount = 1;

        // Maximum 2^RunLimit seconds of wait time due to retries.
        while (runCount <= RunLimit)
        {
            try
            {
                if (fileSystem.File.Exists(filePath))
                {
                    // There are a number of reasons why "ReadAllBytes" could fail with IOException.
                    // DirectoryNotFound, EndOfStream, FileNotFound, FileLoad, PathTooLong, etc.
                    // However, one benefit to "ReadAllBytes" versus "OpenRead" is that "ReadAllBytes"
                    // will closes the file handle once the operation is complete. This helps mitigate
                    // some instances of the IO Exception "The process cannot access the file because
                    // it is being used by another process."
                    // https://learn.microsoft.com/en-us/dotnet/api/system.io.file.openread#exceptions
                    // https://learn.microsoft.com/en-us/dotnet/api/system.io.ioexception#remarks
                    byte[] fileContents = fileSystem.File.ReadAllBytes(filePath);
                    return SHA256.Create().ComputeHash(fileContents);
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
                if (runCount == RunLimit)
                {
                    throw;
                }

                Thread.Sleep(TimeSpan.FromSeconds(Math.Pow(ExponentialRetryBase, runCount)));
                runCount++;
            }
        }

#if NET8_0_OR_GREATER
        return new byte[SHA256.HashSizeInBytes];
#else
        return new byte[32];
#endif
    }
}
