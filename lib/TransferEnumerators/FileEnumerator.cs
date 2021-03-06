﻿//------------------------------------------------------------------------------
// <copyright file="FileEnumerator.cs" company="Microsoft">
//    Copyright (c) Microsoft Corporation
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.WindowsAzure.Storage.DataMovement.TransferEnumerators
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Threading;

    /// <summary>
    /// Transfer enumerator for file system.
    /// </summary>
    internal class FileEnumerator : TransferEnumeratorBase, ITransferEnumerator
    {
        /// <summary>
        /// Maximum windows file path is 260 characters, including a terminating NULL characters.
        /// This leaves 259 useable characters.
        /// </summary>
        // TODO - Windows file path has 2 limits.
        //   1) Full file name can not be longer than 259 characters. 
        //   2) Folder path can not be longer than 247 characters excluding the file name. 
        // If folder path is longer than 247 characters, it will fail at creating the directory.
        // This way, there will be no trash left.
        private const int MaxFilePathLength = 259;

        private const string DefaultFilePattern = "*";

        private DirectoryLocation location;

        private FileListContinuationToken listContinuationToken;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileEnumerator" /> class.
        /// </summary>
        /// <param name="location">Directory location.</param>
        public FileEnumerator(DirectoryLocation location)
        {
            this.location = location;
        }

        /// <summary>
        /// Gets or sets the enumerate continulation token.
        /// </summary>
        public ListContinuationToken EnumerateContinuationToken
        {
            get
            {
                return this.listContinuationToken;
            }

            set
            {
                Debug.Assert(value is FileListContinuationToken);
                this.listContinuationToken = value as FileListContinuationToken;
            }
        }

        /// <summary>
        /// Enumerates the files present in the storage location referenced by this object.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken to cancel the method.</param>
        /// <returns>Enumerable list of TransferEntry objects found in the storage location referenced by this object.</returns>
        public IEnumerable<TransferEntry> EnumerateLocation(CancellationToken cancellationToken)
        {
            Utils.CheckCancellation(cancellationToken);

            string filePattern = string.IsNullOrEmpty(this.SearchPattern) ? DefaultFilePattern : this.SearchPattern;

            // Exceed-limit-length patterns surely match no files.
            int maxFileNameLength = this.GetMaxFileNameLength();
            if (filePattern.Length > maxFileNameLength)
            {
                yield break;
            }

            SearchOption searchOption = this.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            IEnumerable<string> directoryEnumerator = null;
            ErrorEntry errorEntry = null;

            Utils.CheckCancellation(cancellationToken);

            string fullPath = Path.GetFullPath(this.location.DirectoryPath);
            fullPath = AppendDirectorySeparator(fullPath);

            try
            {
                // Directory.GetFiles/EnumerateFiles will be broken when encounted special items, such as
                // files in recycle bins or the folder "System Volume Information". Rewrite this function
                // because our listing should not be stopped by these unexpected files.
                directoryEnumerator = EnumerateDirectoryHelper.EnumerateFiles(
                    fullPath,
                    filePattern,
                    this.listContinuationToken == null ? null : this.listContinuationToken.FilePath,
                    searchOption,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                string errorMessage = string.Format(
                    CultureInfo.CurrentCulture,
                    Resources.FailedToEnumerateDirectory,
                    this.location.DirectoryPath,
                    filePattern);

                TransferException exception =
                    new TransferException(TransferErrorCode.FailToEnumerateDirectory, errorMessage, ex);
                errorEntry = new ErrorEntry(exception);
            }

            if (null != errorEntry)
            {
                // We any exception we might get from Directory.GetFiles/
                // Directory.EnumerateFiles. Just return an error entry
                // to indicate error occured in this case. 
                yield return errorEntry;
            }

            if (null != directoryEnumerator)
            {
                foreach (string entry in directoryEnumerator)
                {
                    Utils.CheckCancellation(cancellationToken);

                    string relativePath = entry;

                    if (relativePath.StartsWith(fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = relativePath.Remove(0, fullPath.Length);
                    }

                    yield return new FileEntry(
                        relativePath,
                        Path.Combine(this.location.DirectoryPath, relativePath),
                        new FileListContinuationToken(relativePath));
                }
            }
        }

        private static string AppendDirectorySeparator(string dir)
        {
            char lastC = dir[dir.Length - 1];
            if (Path.DirectorySeparatorChar != lastC && Path.AltDirectorySeparatorChar != lastC)
            {
                dir = dir + Path.DirectorySeparatorChar;
            }

            return dir;
        }

        /// <summary>
        /// Gets the maximum file name length of any file name relative to this file system source location. 
        /// </summary>
        /// <returns>Maximum file name length in bytes.</returns>
        private int GetMaxFileNameLength()
        {
            return MaxFilePathLength - this.location.DirectoryPath.Length;
        }
    }
}
