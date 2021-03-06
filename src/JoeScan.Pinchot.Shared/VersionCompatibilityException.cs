﻿// Copyright(c) JoeScan Inc. All Rights Reserved.
//
// Licensed under the BSD 3 Clause License. See LICENSE.txt in the project
// root for license information.

using System;
using System.Runtime.Serialization;

namespace JoeScan.Pinchot
{
    [Serializable]
    internal class VersionCompatibilityException : InvalidOperationException
    {
        internal static string GetErrorReason(ScanHeadVersionInformation version)
        {
            var scanHeadVersion = $"{version.Major}.{version.Minor}.{version.Patch}";
            var apiVersion = $"{VersionInformation.Major}.{VersionInformation.Minor}.{VersionInformation.Patch}";
            var err = $"Scan head version {scanHeadVersion} is not compatible with API version {apiVersion}";
            return err;
        }

        internal VersionCompatibilityException()
        {
        }

        internal VersionCompatibilityException(ScanHeadVersionInformation version)
            : base(GetErrorReason(version))
        {
        }

        internal VersionCompatibilityException(string message)
            : base(message)
        {
        }

        internal VersionCompatibilityException(string message, Exception inner)
            : base(message, inner)
        {
        }

        protected VersionCompatibilityException(SerializationInfo serializationInfo, StreamingContext streamingContext)
            : base(serializationInfo, streamingContext)
        {
        }
    }
}