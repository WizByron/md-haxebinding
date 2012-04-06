// This file is part of the MonoDevelop Flex Language Binding.
//
// Copyright (c) 2009 Studio Associato Di Nunzio e Di Gregorio
//
//  Authors:
//     Federico Di Gregorio <fog@initd.org>
//
// This source code is licenced under The MIT License.
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;
using MonoDevelop.Projects;
using MonoDevelop.Core;
using MonoDevelop.HaxeBinding.Projects;

namespace MonoDevelop.HaxeBinding.Tools
{
    static class HaxeCompilerManager
    {
        public static BuildResult Compile(HaxeProject project, HaxeProjectConfiguration configuration, IProgressMonitor monitor)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendFormat(" -output \"{0}/{1}\" ", configuration.OutputDirectory, configuration.OutputFileName);

            if (configuration.DebugMode)
                sb.Append("-compiler.debug=true ");

            if (!String.IsNullOrEmpty(configuration.CompilerParameters)) {
                sb.Append(configuration.CompilerParameters);
                sb.Append(" ");
            }

            sb.Append(project.MainSource);

            string output = "";
            string error  = "";

            string cmd = Path.Combine(Path.Combine(PropertyService.Get<string>("CBinding.FlexSdkPath"), "bin"), project.Compiler);
            string args = sb.ToString();
            monitor.Log.WriteLine(cmd+" "+args);

            int exitCode = DoCompilation(cmd, args, project.BaseDirectory, ref output, ref error);

            BuildResult result = ParseOutput(output, error);
            if (result.CompilerOutput.Trim().Length != 0)
                monitor.Log.WriteLine(result.CompilerOutput);

            // If compiler crashes, output entire error string.

            if (result.ErrorCount == 0 && exitCode != 0) {
                if (!string.IsNullOrEmpty(error))
                    result.AddError(error);
                else
                    result.AddError("The compiler appears to have crashed without any error output.");
            }

            FileService.DeleteFile(output);
            FileService.DeleteFile(error);
            return result;
        }

        static void ParserOutputFile(BuildResult result, StringBuilder output, string filename)
        {
            StreamReader reader = File.OpenText(filename);

            string line;
            while ((line = reader.ReadLine()) != null) {
                output.AppendLine(line);

                line = line.Trim();
                if (line.Length == 0 || line.StartsWith("\t"))
                    continue;

                BuildError error = CreateErrorFromString(line);
                if (error != null)
                    result.Append(error);
            }

            reader.Close();
        }

        static BuildResult ParseOutput(string stdout, string stderr)
        {
            BuildResult result = new BuildResult();

            StringBuilder output = new StringBuilder();

            ParserOutputFile(result, output, stdout);
            ParserOutputFile(result, output, stderr);

            result.CompilerOutput = output.ToString();

            return result;
        }

        static int DoCompilation(string cmd, string args, string wd, ref string output, ref string error)
        {
            int exitcode = 0;

            output = Path.GetTempFileName();
            error  = Path.GetTempFileName();

            StreamWriter outwr = new StreamWriter(output);
            StreamWriter errwr = new StreamWriter(error);

            ProcessStartInfo pinfo = new ProcessStartInfo(cmd, args);
            pinfo.UseShellExecute = false;
            pinfo.RedirectStandardOutput = true;
            pinfo.RedirectStandardError = true;
            pinfo.WorkingDirectory = wd;

            using (MonoDevelop.Core.Execution.ProcessWrapper pw = Runtime.ProcessService.StartProcess(pinfo, outwr, errwr, null)) {
                pw.WaitForOutput();
                exitcode = pw.ExitCode;
            }
            outwr.Close();
            errwr.Close();

            return exitcode;
        }

        static Regex mErrorFull = new Regex(@"^(?<file>.+)\((?<line>\d+)\):\s(col:\s)?(?<column>\d*)\s?(?<level>\w+):\s(?<message>.*)\.?$",
                                            RegexOptions.Compiled | RegexOptions.ExplicitCapture);
        static Regex mErrorFile = new Regex(@"^(?<file>.+):\s(?<level>\w+):\s(?<message>.*)\.?$",
                                            RegexOptions.Compiled | RegexOptions.ExplicitCapture);
        static Regex mErrorCmdLine = new Regex(@"^command line: (?<level>\w+):\s(?<message>.*)\.?$",
                                              RegexOptions.Compiled | RegexOptions.ExplicitCapture);
        static Regex mErrorSimple = new Regex(@"^(?<level>\w+):\s(?<message>.*)\.?$",
                                              RegexOptions.Compiled | RegexOptions.ExplicitCapture);
        static Regex mErrorIgnore = new Regex(@"^(Updated|Recompile|Reason|Files changed):.*",
                                              RegexOptions.Compiled);

        static BuildError CreateErrorFromString(string text)
        {
            Match match = mErrorIgnore.Match(text);
            if (match.Success)
                return null;

            match = mErrorFull.Match(text);
            if (!match.Success)
                match = mErrorCmdLine.Match(text);
            if (!match.Success)
                match = mErrorFile.Match(text);
            if (!match.Success)
                match = mErrorSimple.Match(text);
            if (!match.Success)
                return null;

            int n;

            BuildError error = new BuildError();
            error.FileName = match.Result("${file}") ?? "";
            error.IsWarning = match.Result("${level}").ToLower() == "warning";
            error.ErrorText = match.Result("${message}");

            if (error.FileName == "${file}")
                error.FileName = "";

            if (Int32.TryParse(match.Result("${line}"), out n))
                error.Line = n;
            else
                error.Line = 0;

            if (Int32.TryParse(match.Result("${column}"), out n))
                error.Column = n;
            else
                error.Column = -1;

            return error;
        }
    }
}

