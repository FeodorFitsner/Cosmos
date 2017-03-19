﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Cosmos.Build.Common;

namespace Cosmos.TestRunner.Core
{
    partial class Engine
    {
        private string FindCosmosRoot()
        {
            var xCurrentDirectory = AppContext.BaseDirectory;
            var xCurrentInfo = new DirectoryInfo(xCurrentDirectory);
            while (xCurrentInfo.Parent != null)
            {
                if (xCurrentInfo.GetDirectories("source").Any())
                {
                    return xCurrentDirectory;
                }
                xCurrentInfo = xCurrentInfo.Parent;
                xCurrentDirectory = xCurrentInfo.FullName;
            }
            return string.Empty;
        }

        private void RunDotnetPublish(string aProjectPath, string aOutputPath, string aRuntimeTarget)
        {
            bool xResult = true;
            var xArgsString = $"publish \"{aProjectPath}\" -o \"{aOutputPath}\" -r {aRuntimeTarget}";

            RunProcess("dotnet", aProjectPath, xArgsString);
        }

        private void RunProcess(string aProcess, string aWorkingDirectory, List<string> aArguments, bool aAttachDebugger = false)
        {
            if (string.IsNullOrWhiteSpace(aProcess))
            {
                throw new ArgumentNullException(aProcess);
            }

            var xArgsString = aArguments.Aggregate("", (aArgs, aArg) => $"{aArgs} \"{aArg}\"");

            RunProcess(aProcess, aWorkingDirectory, xArgsString, aAttachDebugger);
        }

        private void RunProcess(string aProcess, string aWorkingDirectory, string aArguments, bool aAttachDebugger = false)
        {
            if (string.IsNullOrWhiteSpace(aProcess))
            {
                throw new ArgumentNullException(aProcess);
            }

            if (aAttachDebugger)
            {
                aArguments += " \"AttachVsDebugger:True\"";
            }

            Action<string> xErrorReceived = OutputHandler.LogError;
            Action<string> xOutputReceived = OutputHandler.LogMessage;

            bool xResult;

            var xProcessStartInfo = new ProcessStartInfo
            {
                WorkingDirectory = aWorkingDirectory,
                FileName = aProcess,
                Arguments = aArguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            xOutputReceived($"Executing command line '{aProcess} {aArguments}'");
            xOutputReceived($"Working directory = '{aWorkingDirectory}'");

            using (var xProcess = new Process())
            {
                xProcess.StartInfo = xProcessStartInfo;

                xProcess.ErrorDataReceived += delegate (object aSender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        xErrorReceived(e.Data);
                    }
                };
                xProcess.OutputDataReceived += delegate (object aSender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        xOutputReceived(e.Data);
                    }
                };

                xProcess.Start();
                xProcess.BeginErrorReadLine();
                xProcess.BeginOutputReadLine();
                xProcess.WaitForExit(AllowedSecondsInKernel * 1000);

                if (!xProcess.HasExited)
                {
                    xProcess.Kill();
                    xErrorReceived($"'{aProcess}' timed out.");
                }
                else
                {
                    if (xProcess.ExitCode != 0)
                    {
                        xErrorReceived($"Error invoking '{aProcess}'.");
                    }
                }
                xResult = true;
            }

            if (!xResult)
            {
                throw new Exception("Error running process!");
            }
        }

        public static string RunObjDump(string cosmosBuildDir, string workingDir, string inputFile, Action<string> errorReceived, Action<string> outputReceived)
        {
            var xMapFile = Path.ChangeExtension(inputFile, "map");
            File.Delete(xMapFile);
            if (File.Exists(xMapFile))
            {
                throw new Exception("Could not delete " + xMapFile);
            }

            var xTempBatFile = Path.Combine(workingDir, "ExtractElfMap.bat");
            File.WriteAllText(xTempBatFile, "@ECHO OFF\r\n\"" + Path.Combine(cosmosBuildDir, @"tools\cygwin\objdump.exe") + "\" --wide --syms \"" + inputFile + "\" > \"" + Path.GetFileName(xMapFile) + "\"");

            var xProcessStartInfo = new ProcessStartInfo
            {
                WorkingDirectory = workingDir,
                FileName = xTempBatFile,
                Arguments = "",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var xProcess = Process.Start(xProcessStartInfo);

            xProcess.WaitForExit(20000);

            File.Delete(xTempBatFile);

            return xMapFile;
        }

        private void RunExtractMapFromElfFile(string workingDir, string kernelFileName)
        {
            RunObjDump(CosmosPaths.Build, workingDir, kernelFileName, OutputHandler.LogError, OutputHandler.LogMessage);
        }

        private void RunIL2CPU(string kernelFileName, string outputFile)
        {
            bool xUsingUserkit = false;
            string xIL2CPUPath = Path.Combine(FindCosmosRoot(), "source\\IL2CPU");
            if (!Directory.Exists(xIL2CPUPath))
            {
                xUsingUserkit = true;
                xIL2CPUPath = GetCosmosUserkitFolder();
            }
            if (!Directory.Exists(xIL2CPUPath))
            {
                throw new DirectoryNotFoundException("IL2CPU not found.");
            }

            //if (!xUsingUserkit)
            //{
            //    RunDotnetPublish(xIL2CPUPath, AppContext.BaseDirectory, "win7-x86");
            //    xIL2CPUPath = AppContext.BaseDirectory;
            //}

            References = new List<string>
            {
                kernelFileName,
                "Cosmos.Core.Plugs.Asm.dll",
                "Cosmos.Debug.Kernel.Plugs.Asm.dll"
            };

            var xArguments = new List<string>
            {
                "DebugEnabled:true",
                "StackCorruptionDetectionEnabled:" + EnableStackCorruptionChecks,
                "StackCorruptionDetectionLevel:" + StackCorruptionChecksLevel,
                "DebugMode:Source",
                "TraceAssemblies:" + TraceAssembliesLevel,
                "DebugCom:1",
                "UseNAsm:True",
                "OutputFilename:" + outputFile,
                "EnableLogging:True",
                "EmitDebugSymbols:True",
                "IgnoreDebugStubAttribute:False"
            };
            xArguments.AddRange(References.Select(aReference => "References:" + aReference));

            if (DebugIL2CPU)
            {
                if (KernelsToRun.Count > 1)
                {
                    throw new Exception("Cannot run multiple kernels with in-process compilation!");
                }

                // ensure we're using the referenced (= solution) version
                Cosmos.IL2CPU.CosmosAssembler.ReadDebugStubFromDisk = false;
            }

            if (xUsingUserkit)
            {
                RunProcess("IL2CPU", xIL2CPUPath, xArguments, DebugIL2CPU);
            }
            else
            {
                xArguments.Insert(0, "run");
                xArguments.Insert(1, " -- ");
                RunProcess("dotnet", xIL2CPUPath, xArguments);
            }
        }

        private void RunNasm(string inputFile, string outputFile, bool isElf)
        {
            bool xUsingUserkit = false;
            string xNasmPath = Path.Combine(FindCosmosRoot(), "Tools\\NASM");
            if (!Directory.Exists(xNasmPath))
            {
                xNasmPath = Path.Combine(GetCosmosUserkitFolder(), "Tools");
            }
            if (!Directory.Exists(xNasmPath))
            {
                throw new DirectoryNotFoundException("NASM path not found.");
            }

            //if (!xUsingUserkit)
            //{
            //    RunDotnetPublish(xNasmPath, AppContext.BaseDirectory, "win7-x86");
            //    xNasmPath = AppContext.BaseDirectory;
            //}

            var xArgs = new List<string>
            {
                $"InputFile:{inputFile}",
                $"OutputFile:{outputFile}",
                $"IsELF:{isElf}"
            };

            if (xUsingUserkit)
            {
                RunProcess("NASM", xNasmPath, xArgs);
            }
            else
            {
                xArgs.Insert(0, "run");
                xArgs.Insert(1," -- ");
                RunProcess("dotnet", xNasmPath, xArgs);
            }
        }

        private void RunLd(string inputFile, string outputFile)
        {
            string[] arguments = new[]
                       {
                           "-Ttext", "0x2000000",
                           "-Tdata", " 0x1000000",
                           "-e", "Kernel_Start",
                           "-o",outputFile.Replace('\\', '/'),
                           inputFile.Replace('\\', '/')
                       };

            var xArgsString = arguments.Aggregate("", (a, b) => a + " \"" + b + "\"");

            var xProcess = Process.Start(Path.Combine(GetCosmosUserkitFolder(), "build", "tools", "cygwin", "ld.exe"), xArgsString);

            xProcess.WaitForExit(10000);

            //RunProcess(Path.Combine(GetCosmosUserkitFolder(), "build", "tools", "cygwin", "ld.exe"),
            //           mBaseWorkingDirectory,
            //           new[]
            //           {
            //               "-Ttext", "0x2000000",
            //               "-Tdata", " 0x1000000",
            //               "-e", "Kernel_Start",
            //               "-o",outputFile.Replace('\\', '/'),
            //               inputFile.Replace('\\', '/')
            //           });
        }

        private static string GetCosmosUserkitFolder()
        {
            CosmosPaths.Initialize();
            return CosmosPaths.UserKit;
        }

        private void MakeIso(string objectFile, string isoFile)
        {
            IsoMaker.Generate(objectFile, isoFile);
            if (!File.Exists(isoFile))
            {
                throw new Exception("Error building iso");
            }
        }
    }
}
