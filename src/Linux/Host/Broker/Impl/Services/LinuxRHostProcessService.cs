﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Microsoft.Common.Core.IO;
using Microsoft.Common.Core.OS;
using Microsoft.Extensions.Logging;
using Microsoft.R.Host.Broker.Interpreters;
using Microsoft.R.Host.Broker.Sessions;
using Microsoft.R.Host.Broker.Security;

namespace Microsoft.R.Host.Broker.Services {
    class LinuxRHostProcessService : IRHostProcessService {
        private readonly ILogger<Session> _sessionLogger;
        private readonly IProcessServices _ps;
        private readonly IFileSystem _fs;

        public LinuxRHostProcessService(ILogger<Session> sessionLogger, IFileSystem fs, IProcessServices ps) {
            _sessionLogger = sessionLogger;
            _ps = ps;
            _fs = fs;
        }

        public IProcess StartHost(Interpreter interpreter, string profilePath, string userName, ClaimsPrincipal principal, string commandLine) {
            var args = ParseArgumentsIntoList(commandLine);
            var environment = GetHostEnvironment(interpreter, profilePath, userName);
            var password = principal.FindFirst(UnixClaims.RPassword).Value;

            Process process = Utility.AuthenticateAndRunAsUser(_sessionLogger, _ps, userName, password, profilePath, args, environment);
            process.WaitForExit(250);
            if (process.HasExited && process.ExitCode < 0) {
                var message = _ps.MessageFromExitCode(process.ExitCode);
                if (!string.IsNullOrEmpty(message)) {
                    throw new Win32Exception(message);
                }
                throw new Win32Exception(process.ExitCode);
            }

            return new UnixProcess(process);
        }

        private string GetLdLibraryPath(string rBinPath) {
            string ldLibPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            if (string.IsNullOrEmpty(ldLibPath)) {
                return rBinPath;
            }
            return $"{rBinPath}:{ldLibPath}";
        }

        private IDictionary<string, string> GetHostEnvironment(Interpreter interpreter, string profilePath, string userName) {
            string siteLibrary = string.Join(":", interpreter.RInterpreterInfo.SiteLibraryDirs);

            // TODO: LD_LIBRARY_PATH should be set in R-Host where we call dlopen.
            string loadLibraryPath = GetLdLibraryPath(interpreter.RInterpreterInfo.BinPath);

            Dictionary<string, string> environment = new Dictionary<string, string>() {
                { "HOME"                    , profilePath},
                { "LD_LIBRARY_PATH"         , loadLibraryPath},
                { "LN_S"                    , GetCurrentOrDefault("LN_S")},
                { "PATH"                    , Environment.GetEnvironmentVariable("PATH")},
                { "PWD"                     , profilePath},
                { "R_ARCH"                  , GetCurrentOrDefault("R_ARCH")},
                { "R_BROWSER"               , GetCurrentOrDefault("R_BROWSER")},
                { "R_BZIPCMD"               , GetCurrentOrDefault("R_BZIPCMD")},
                { "R_DOC_DIR"               , interpreter.RInterpreterInfo.DocPath},
                { "R_GZIPCMD"               , GetCurrentOrDefault("R_GZIPCMD")},
                { "R_HOME"                  , interpreter.RInterpreterInfo.InstallPath},
                { "R_INCLUDE_DIR"           , interpreter.RInterpreterInfo.IncludePath},
                { "R_LIBS_SITE"             , siteLibrary},
                { "R_PAPERSIZE"             , GetCurrentOrDefault("R_PAPERSIZE")},
                { "R_PAPERSIZE_USER"        , GetCurrentOrDefault("R_PAPERSIZE_USER")},
                { "R_PDFVIEWER"             , GetCurrentOrDefault("R_PDFVIEWER")},
                { "R_PRINTCMD"              , GetCurrentOrDefault("R_PRINTCMD")},
                { "R_RD4PDF"                , GetCurrentOrDefault("R_RD4PDF")},
                { "R_SHARE_DIR"             , interpreter.RInterpreterInfo.RShareDir},
                { "R_TEXI2DVICMD"           , GetCurrentOrDefault("R_TEXI2DVICMD")},
                { "R_UNZIPCMD"              , GetCurrentOrDefault("R_UNZIPCMD")},
                { "R_ZIPCMD"                , GetCurrentOrDefault("R_ZIPCMD")},
                { "SED"                     , GetCurrentOrDefault("SED")},
                { "SHELL"                   , GetCurrentOrDefault("SHELL")},
                { "SHLVL"                   , GetCurrentOrDefault("SHLVL")},
                { "TAR"                     , GetCurrentOrDefault("TAR")},
                { "USER"                    , Utility.GetUnixUserName(userName)},
            };
            return environment;
        }

        private static string GetCurrentOrDefault(string key) {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrEmpty(value) && !_defaultEnvironment.TryGetValue(key, out value)) {
                return string.Empty;
            }
            return value;
        }
        private static readonly IDictionary<string, string> _defaultEnvironment = new Dictionary<string,string>(){
            { "LN_S"                    , "ln -s"},
            {"R_ARCH"                  , ""},
            {"R_BROWSER"               , "xdg-open"},
            {"R_BZIPCMD"               , "/bin/bzip2"},
            {"R_GZIPCMD"               , "/bin/gzip -n"},
            {"R_PAPERSIZE"             , "letter"},
            {"R_PAPERSIZE_USER"        , "letter"},
            {"R_PDFVIEWER"             , "/usr/bin/xdg-open"},
            {"R_PRINTCMD"              , "/usr/bin/lpr"},
            {"R_RD4PDF"                , "times,inconsolata,hyper"},
            {"R_TEXI2DVICMD"           , "/usr/bin/texi2dvi"},
            {"R_UNZIPCMD"              , "/usr/bin/unzip"},
            {"R_ZIPCMD"                , "/usr/bin/zip"},
            { "SED"                     , "/bin/sed"},
            { "SHELL"                   , "/bin/bash"},
            { "SHLVL"                   , "2"},
            { "TAR"                     , "/bin/tar"},
        };

        private static IEnumerable<string> ParseArgumentsIntoList(string arguments) {
            List<string> results = new List<string>();
            var currentArgument = new StringBuilder();
            bool inQuotes = false;

            // Iterate through all of the characters in the argument string.
            for (int i = 0; i < arguments.Length; i++) {
                // From the current position, iterate through contiguous backslashes.
                int backslashCount = 0;
                for (; i < arguments.Length && arguments[i] == '\\'; i++, backslashCount++) ;
                if (backslashCount > 0) {
                    if (i >= arguments.Length || arguments[i] != '"') {
                        // Backslashes not followed by a double quote:
                        // they should all be treated as literal backslashes.
                        currentArgument.Append('\\', backslashCount);
                        i--;
                    } else {
                        // Backslashes followed by a double quote:
                        // - Output a literal slash for each complete pair of slashes
                        // - If one remains, use it to make the subsequent quote a literal.
                        currentArgument.Append('\\', backslashCount / 2);
                        if (backslashCount % 2 == 0) {
                            i--;
                        } else {
                            currentArgument.Append('"');
                        }
                    }
                    continue;
                }

                char c = arguments[i];

                // If this is a double quote, track whether we're inside of quotes or not.
                // Anything within quotes will be treated as a single argument, even if
                // it contains spaces.
                if (c == '"') {
                    inQuotes = !inQuotes;
                    continue;
                }

                // If this is a space/tab and we're not in quotes, we're done with the current
                // argument, and if we've built up any characters in the current argument,
                // it should be added to the results and then reset for the next one.
                if ((c == ' ' || c == '\t') && !inQuotes) {
                    if (currentArgument.Length > 0) {
                        results.Add(currentArgument.ToString());
                        currentArgument.Clear();
                    }
                    continue;
                }

                // Nothing special; add the character to the current argument.
                currentArgument.Append(c);
            }

            // If we reach the end of the string and we still have anything in our current
            // argument buffer, treat it as an argument to be added to the results.
            if (currentArgument.Length > 0) {
                results.Add(currentArgument.ToString());
            }

            return results;
        }
    }
}