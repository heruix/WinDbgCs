# `CsDebugScript`: C# debugging automation tool
Set of .NET libraries that provide access to different debugging tools.
CsDebugScript.Engine provides framework for writting .NET code against different debuggers/dump processing.
Extension libraries provide access to dbgeng.dll for processing Windows dumps, ability to read Linux core dumps, WinDbg/VisualStudio extension with interactive scripting support.
Debugging both native and managed code is supported (currently, managed code is supported only for dbgeng.dll and WinDbg extension).

### Latest status
[![Build status](https://ci.appveyor.com/api/projects/status/d2j4lxglq0tl1x1i/branch/master?svg=true)](https://ci.appveyor.com/project/southpolenator/windbgcs/branch/master)
[![Code coverage](https://img.shields.io/codecov/c/github/southpolenator/WinDbgCs.svg)](https://codecov.io/github/southpolenator/WinDbgCs)
[![Nuget version](https://img.shields.io/nuget/v/csdebugscript.engine.svg?style=flat)](https://www.nuget.org/packages/csdebugscript.engine/)
[![GitHub release](https://img.shields.io/github/release/southpolenator/windbgcs.svg?style=flat)](https://github.com/southpolenator/WinDbgCs/releases/latest)

If you want newer build than what is available in Releases page or as nuget package, you can click on [Latest build](https://ci.appveyor.com/project/southpolenator/windbgcs/branch/master), select Configuration and click on Artifacts. You can also use private nuget feed [https://ci.appveyor.com/nuget/windbgcs-raewbx34ffcq](https://ci.appveyor.com/nuget/windbgcs-raewbx34ffcq).

# Easy start for using engine
* Create a new .NET project (you can use Console Application)
* Add NuGet package [CsDebugScript](https://www.nuget.org/packages/CsDebugScript)
* Start using it:

```cs
using CsDebugScript;

DebuggerInitialization.OpenDump("path_to_dump_file", "symbol_path;srv*");
// After this line, you can execute any code that can be executed in the script. For example:
foreach (Module module in Process.Current.Modules)
    Console.WriteLine(module.Name);
```

# Wiki
Take a look a [Wiki page](https://github.com/southpolenator/WinDbgCs/wiki) to get hooked up :)  
Or jump to [WinDbg interactive mode screenshots](https://github.com/southpolenator/WinDbgCs/wiki/WinDbg-interactive-mode-screenshots)...

# Code reference
Now that you are hooked up, you can take a look at [code reference](http://southpolenator.github.io/WinDbgCsReference/).

# Prerequisites for building the project
0. [.NET core 2.0](https://www.microsoft.com/net/download/core)
1. [Visual Studio Community 2017](https://www.visualstudio.com/downloads/) (for building WinDbg extension, VisualStudio extension)
