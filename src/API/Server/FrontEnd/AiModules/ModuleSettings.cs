﻿using System;
using System.IO;

using CodeProject.AI.API.Common;
using CodeProject.AI.SDK.Common;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeProject.AI.API.Server.Frontend
{
    /// <summary>
    /// Manages the settings for the background process module runner.
    /// </summary>
    public class ModuleSettings
    {
        // marker for path substitution
        const string RootPathMarker                = "%ROOT_PATH%";
        const string RuntimesPathMarker            = "%RUNTIMES_PATH%";
        const string PreinstalledModulesPathMarker = "%PREINSTALLED_MODULES_PATH%";
        const string ModulesPathMarker             = "%MODULES_PATH%";
        const string CurrentModulePathMarker       = "%CURRENT_MODULE_PATH%";
        const string PlatformMarker                = "%PLATFORM%";
        const string OSMarker                      = "%OS%";  
        const string DataDirMarker                 = "%DATA_DIR%";
        const string PythonPathMarker              = "%PYTHON_PATH%";
        const string PythonRuntimeMarker           = "%PYTHON_RUNTIME%";

        private readonly ServerOptions           _serverOptions;
        private readonly ModuleOptions           _moduleOptions;
        private readonly ILogger<ModuleSettings> _logger;
        private readonly string?                 _appDataDirectory;
         
        /// <summary>
        /// Gets a value indicating whether to launch the backend AI analysis modules.
        /// </summary>
        public bool LaunchModules => _moduleOptions.LaunchModules ?? true;

        /// <summary>
        /// Gets a value indicating the number of seconds to delay the start of launching the 
        /// backend AI modules in order to given the server enough time to properly start up.
        /// </summary>
        public int DelayBeforeLaunchingModulesSecs => _moduleOptions.DelayBeforeLaunchingModulesSecs ?? 0;

        /// <summary>
        /// Gets a value indicating the delay between stopping the background services and passing
        /// control back to the server so it can stop. Ensures modules have time to stop properly
        /// </summary>
        public int DelayAfterStoppingModulesSecs => _moduleOptions.DelayAfterStoppingModulesSecs ?? 3;

        /// <summary>
        /// Gets or sets the root directory that contains the runtimes (eg Python interpreter).
        /// </summary>
        public string? RuntimesPath  => _moduleOptions.RuntimesPath!;

        /// <summary>
        /// Gets the absolute path to the AI modules that were pre-installed when the server was
        /// setup. For instance, during a Docker image build.
        /// </summary>
        public string PreInstalledModulesPath => _moduleOptions.PreInstalledModulesPath!;

        /// <summary>
        /// Gets the absolute path to the AI modules that have been downloaded and installed.
        /// </summary>
        public string ModulesPath => _moduleOptions.ModulesPath!;

        /// <summary>
        /// Gets the absolute path to the download modules zip packages that have been
        /// downloaded from the modules registry
        /// </summary>
        public string DownloadedModulePackagesPath => _moduleOptions.DownloadedModulePackagesPath!;

        /// <summary>
        /// Gets the path to the modules installer script. This will be a batch file or bash file
        /// depending on the current operating system. This script runs from the app's root directory
        /// but has the *modules* directory as the working directory. This script will find the 
        /// module's install script, and run that, initiating the install.
        /// </summary>
        public string ModuleInstallerScriptPath
        {
            get
            {
                if (SystemInfo.OperatingSystem.EqualsIgnoreCase("windows"))
                    return _moduleOptions.ModuleInstallerScriptsPath + "\\setup.bat";

                return _moduleOptions.ModuleInstallerScriptsPath + "/setup.sh";
            }
        }

        /// <summary>
        /// Gets the directory that is the root of this system. 
        /// </summary>
        /// <param name="configRootPath">The root path specified in the config file.
        /// assumptions</param>
        /// <returns>A string for the adjusted path</returns>
        public static string GetRootPath(string? configRootPath)
        {
            string defaultPath = configRootPath ?? AppContext.BaseDirectory;

            // Correct for cross platform (win = \, linux = /)
            defaultPath = Text.FixSlashes(defaultPath);

            // Either the config file or lets assume it's the current dir if all else fails
            string rootPath = defaultPath;

            // If the config value is a relative path then add it to the current dir. This is where
            // we have to trust the config values are right, and we also have to trust that when
            // this server is called the "ASPNETCORE_ENVIRONMENT" flag is set as necessary in order
            // to ensure the appsettings.Development.json config files are included
            if (rootPath.StartsWith(".."))
                rootPath = Path.Combine(AppContext.BaseDirectory, rootPath!);

            // converts relative URLs and squashes the path to he correct absolute path
            rootPath = Path.GetFullPath(rootPath);

            // HACK: If we're running this server from the build output dir in dev environment
            // then the root path will be wrong.
            if (SystemInfo.IsDevelopmentCode)
            {
                DirectoryInfo? info = new DirectoryInfo(rootPath);
                while (info != null)
                {
                    info = info.Parent;
                    if (info?.Name.ToLower() == "server")
                    {
                        info = info.Parent; // This will be the root in the installed version

                        // For debug / dev environment, the parent is API, followed by src
                        if (info?.Name.ToLower() == "api")
                        {
                            info = info.Parent;
                            if (info?.Name.ToLower() == "src")
                                info = info.Parent;
                            else
                                info = null; // We should have seen "src" for development code
                        }
                        break;
                    }
                }

                if (info != null)
                    rootPath = info.FullName;
            }

            return rootPath;
        }

        /// <summary>
        /// Adds a module's modulesettings.*.json files to a configuration builder in the correct
        /// order, taking into account the environment and platform.
        /// </summary>
        /// <param name="config">The IConfigurationBuilder object</param>
        /// <param name="modulePath">The directory containing the module</param>
        /// <param name="reloadOnChange">Whether to trigger a reload if the files change</param>
        public static void LoadModuleSettings(IConfigurationBuilder config, string modulePath,
                                              bool reloadOnChange)
        {
            string runtimeEnv   = SystemInfo.RuntimeEnvironment == RuntimeEnvironment.Development ||
                                  SystemInfo.IsDevelopmentCode ? "development" : string.Empty;
            string os           = SystemInfo.OperatingSystem.ToLower();
            string architecture = SystemInfo.Architecture.ToLower();

            // modulesettings.json
            // modulesettings.development.json
            // modulesettings.os.json
            // modulesettings.os.development.json
            // modulesettings.os.architecture.json
            // modulesettings.os.architecture.development.json
            // modulesettings.docker.json
            // modulesettings.docker.development.json

            string settingsFile = Path.Combine(modulePath, "modulesettings.json");
            if (File.Exists(settingsFile))
                config.AddJsonFile(settingsFile, optional: true, reloadOnChange: reloadOnChange);

            if (!string.IsNullOrEmpty(runtimeEnv))
            {
                settingsFile = Path.Combine(modulePath, $"modulesettings.{runtimeEnv}.json");
                if (File.Exists(settingsFile))
                    config.AddJsonFile(settingsFile, optional: true, reloadOnChange: reloadOnChange);
            }

            settingsFile = Path.Combine(modulePath, $"modulesettings.{os}.json");
            if (File.Exists(settingsFile))
                config.AddJsonFile(settingsFile, optional: true, reloadOnChange: reloadOnChange);

            if (!string.IsNullOrEmpty(runtimeEnv))
            {
                settingsFile = Path.Combine(modulePath, $"modulesettings.{os}.{runtimeEnv}.json");
                if (File.Exists(settingsFile))
                    config.AddJsonFile(settingsFile, optional: true, reloadOnChange: reloadOnChange);
            }

            settingsFile = Path.Combine(modulePath, $"modulesettings.{os}.{architecture}.json");
            if (File.Exists(settingsFile))
                config.AddJsonFile(settingsFile, optional: true, reloadOnChange: reloadOnChange);

            if (!string.IsNullOrEmpty(runtimeEnv))
            {
                settingsFile = Path.Combine(modulePath, $"modulesettings.{os}.{architecture}.{runtimeEnv}.json");
                if (File.Exists(settingsFile))
                    config.AddJsonFile(settingsFile, optional: true, reloadOnChange: reloadOnChange);
            }

            if (SystemInfo.ExecutionEnvironment == ExecutionEnvironment.Docker)
            {
                settingsFile = Path.Combine(modulePath, $"modulesettings.docker.json");
                if (File.Exists(settingsFile))
                    config.AddJsonFile(settingsFile, optional: true, reloadOnChange: reloadOnChange);
            }
        }

        /// <summary>
        /// Initialises a new instance of the ModuleSettings.
        /// </summary>
        /// <param name="config">The application configuration.</param>
        /// <param name="serverOptions">The server Options</param>
        /// <param name="moduleOptions">The module Options</param>
        /// <param name="logger">The logger.</param>
        public ModuleSettings(IConfiguration config,
                              IOptions<ServerOptions> serverOptions,
                              IOptions<ModuleOptions> moduleOptions,
                              ILogger<ModuleSettings> logger)
        {
            _serverOptions = serverOptions.Value;
            _moduleOptions = moduleOptions.Value;
            _logger        = logger;

            // ApplicationDataDir is set in Program.cs and added to an InMemoryCollection config set.
            _appDataDirectory = config.GetValue<string>("ApplicationDataDir");

            ExpandMacros();
        }

        /// <summary>
        /// Returns a string that represents the current directory a module lives in. Note that a
        /// module's folder is always the same name as its Id.
        /// </summary>
        /// <param name="module">The module to launch</param>
        /// <returns>A string object</returns>
        public string GetModulePath(ModuleBase module)
        {
            if (module.PreInstalled)
                return Path.Combine(PreInstalledModulesPath, module.ModuleId!);

            return Path.Combine(ModulesPath, module.ModuleId!);
        }

        /// <summary>
        /// Returns a string that represents the working directory for a module.
        /// </summary>
        /// <remarks>
        /// REVIEW: [Mattew] module.WorkingDirectory is set safely and can be used instead of this if you wish
        /// The working directory isn't necessarily the dir the executed file is in. eg. .NET
        /// exes can be buried deep in /bin/Debug/net6/net6.0-windows. The working directory also
        /// isn't the Module directory, since the actual executable code for a module could be in a
        /// subdirectory of that module. So we start by assuming it's the path where the executed
        /// file is, but allow for an override (in the case of .NET development) if provided.
        /// </remarks>
        /// <param name="module">The module to launch</param>
        /// <returns>A string object</returns>
        public string GetWorkingDirectory(ModuleBase module)
        {
            return GetModulePath(module);
        }

        /// <summary>
        /// Returns a string that represents the command to run to launch a module. Order of 
        /// precedence is the module's Command, the Runtime, and then a guess based on FilePath.
        /// </summary>
        /// <param name="module">The module to launch</param>
        /// <returns>A string object</returns>
        public string? GetCommandPath(ModuleConfig module)
        {
            string? command = ExpandOption(module.Command, GetModulePath(module)) ??
                              GetCommandByRuntime(module) ??
                              GetCommandByFilepath(module);
            return command;
        }

        /// <summary>
        /// Returns a string that represents the absolute path to the file to be launched by a
        /// command to run to launch a module
        /// </summary>
        /// <param name="module">The module to launch</param>
        /// <returns>A string object</returns>
        public string GetFilePath(ModuleConfig module)
        {
            // Correcting for cross platform (win = \, linux = /)
            return Path.Combine(GetModulePath(module), Text.FixSlashes(module.FilePath));
        }

        /// <summary>
        /// Gets the command to run based on the runtime, where the runtime was installed, and the
        /// path to the given module for which the command is to be run
        /// </summary>
        /// <param name="module">The module whose command we're looking to get</param>
        /// <returns>A command that can be run directly on the current OS</returns>
        private string? GetCommandByRuntime(ModuleConfig module)
        {
            if (module is null || module.Runtime is null)
                return null;

            string runtime = module.Runtime.ToLower();
            _logger.LogTrace($"GetCommandByRuntime: Runtime={runtime}, Location={module.RuntimeLocation}");

            // HACK: Ultimately we will have a set of "runtime" modules which will install and
            // register the runtimes we use. The registration will include the runtime name
            // (eg "python39") and the path to the runtime's launcher. For now we're going to 
            // just hardcode Python and .NET support.

            // If it is "Python" then use our default Python location (in this case, python 3.7 or
            // 3.8 if Linux/macOS)
            if (runtime == "python")
                runtime = SystemInfo.OperatingSystem == "Windows" ? "python37" : "python38";

            // HACK: In Docker, Python installs for downloaded modules can be local for downloaded
            // modules, or shared for pre-installed modules. For preinstalled/shared the python
            // command is in the format of python3.N because we don't install Python in the runtimes
            // folder, but in the OS itself. In Docker this means we call "python3.8", rather than 
            // "/runtimes/bin/linux/python38/venv/bin/python3
            if (SystemInfo.ExecutionEnvironment == ExecutionEnvironment.Docker && 
                module.RuntimeLocation == "Shared" && runtime.StartsWith("python"))
            {
                if (!runtime.StartsWith("python3."))
                    runtime = runtime.Replace("python3", "python3.");
                
                return runtime;
            }

            // Everything else is just a straight pass-through (note that 'execute' and 'launcher'
            // are just markers that say 'call the module's file directly - it is runnable')
            if (runtime == "dotnet" || runtime == "execute" || runtime == "launcher")
                return runtime;

            // If it is a Python3X command then replace our marker in the default python path to
            // match the requested interpreter location in order to build the 
            // "/runtimes/bin/linux/python38/venv/bin/python3" path.
            if (runtime.StartsWith("python"))
            {
                string commandPath = _moduleOptions.PythonRelativeInterpreterPath!
                                                   .Replace(PythonRuntimeMarker, runtime);
                commandPath = commandPath.TrimStart('\\','/');
                if (module.RuntimeLocation == "Shared")
                    commandPath = Path.Combine(_moduleOptions.RuntimesPath!, commandPath);
                else
                    commandPath = Path.Combine(GetModulePath(module), commandPath);

                // Correct the path to handle any path traversals (eg ../) in the path
                if (commandPath?.Contains(Path.DirectorySeparatorChar) ?? false)
                    commandPath = Path.GetFullPath(commandPath);

                return commandPath;
            }

            return null;
        }

        private string? GetCommandByFilepath(ModuleConfig module)
        {
            if (module is null || module.FilePath  is null)
                return null;

            // HACK: Ultimately we will have a set of "runtime" modules which will install and
            // register the runtimes we use. The registration will include the runtime name
            // (eg "dotnet") and the file extensions that the runtime can unambiguously handle.
            // The "python39" runtime, for example, may want to register .py, but so would python37.
            // "dotnet" is welcome to register .dll as long as no other runtime module wants .dll too.

            string extension = Path.GetExtension(module.FilePath);
            if (extension == ".py")
                module.Runtime = "python";

            return extension switch
            {
                ".py" => GetCommandByRuntime(module),
                ".dll" => "dotnet",
                ".exe" => "execute",
                _ => throw new Exception("If neither Runtime nor Command is specified then FilePath must have an extension of '.py' or '.dll'."),
            };
        }

        /// <summary>
        /// Expands all the directory markers in the options.
        /// </summary>
        private void ExpandMacros()
        {
            if (_serverOptions is null)
                return;

            // For Macro expansion in appsettings settings we have PYTHON_PATH which depends on
            // PYTHON_RELATIVE_BASEPATH which usually depends on RUNTIMES_PATH and both depend 
            // on ROOT_PATH.GetProcessStatus and expand each of these in the correct order.

            _serverOptions.ApplicationRootPath           = GetRootPath(_serverOptions.ApplicationRootPath);

            _moduleOptions.RuntimesPath                  = Path.GetFullPath(ExpandOption(_moduleOptions.RuntimesPath)!);
            _moduleOptions.ModulesPath                   = Path.GetFullPath(ExpandOption(_moduleOptions.ModulesPath)!);
            _moduleOptions.PreInstalledModulesPath       = Path.GetFullPath(ExpandOption(_moduleOptions.PreInstalledModulesPath)!);
            _moduleOptions.DownloadedModulePackagesPath  = Path.GetFullPath(ExpandOption(_moduleOptions.DownloadedModulePackagesPath)!);
            _moduleOptions.ModuleInstallerScriptsPath    = Path.GetFullPath(ExpandOption(_moduleOptions.ModuleInstallerScriptsPath)!);

            _moduleOptions.PythonRelativeInterpreterPath = ExpandOption(_moduleOptions.PythonRelativeInterpreterPath);

            // Corect the slashes
            _serverOptions.ApplicationRootPath           = Text.FixSlashes(_serverOptions.ApplicationRootPath);
            _moduleOptions.PythonRelativeInterpreterPath = Text.FixSlashes(_moduleOptions.PythonRelativeInterpreterPath);
            _moduleOptions.RuntimesPath                  = Text.FixSlashes(_moduleOptions.RuntimesPath);
            _moduleOptions.PreInstalledModulesPath       = Text.FixSlashes(_moduleOptions.PreInstalledModulesPath);
            _moduleOptions.ModulesPath                   = Text.FixSlashes(_moduleOptions.ModulesPath);
            _moduleOptions.ModuleInstallerScriptsPath    = Text.FixSlashes(_moduleOptions.ModuleInstallerScriptsPath);

            _logger.LogInformation($"ROOT_PATH                 = {_serverOptions.ApplicationRootPath}");
            _logger.LogInformation($"RUNTIMES_PATH             = {_moduleOptions.RuntimesPath}");
            _logger.LogInformation($"PREINSTALLED_MODULES_PATH = {_moduleOptions.PreInstalledModulesPath}");
            _logger.LogInformation($"MODULES_PATH              = {_moduleOptions.ModulesPath}");
            _logger.LogInformation($"PYTHON_PATH               = {_moduleOptions.PythonRelativeInterpreterPath}");
            _logger.LogInformation($"Data Dir                  = {_appDataDirectory}");
        }

        /// <summary>
        /// Expands the directory markers in the string.
        /// </summary>
        /// <param name="value">The value to expand.</param>
        /// <param name="currentModulePath">The path to the current module, if appropriate.</param>
        /// <returns>The expanded path.</returns>
        public string? ExpandOption(string? value, string? currentModulePath = null)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            value = value.Replace(RuntimesPathMarker, _moduleOptions.RuntimesPath);
            value = value.Replace(PreinstalledModulesPathMarker,  _moduleOptions.PreInstalledModulesPath);
            value = value.Replace(ModulesPathMarker,  _moduleOptions.ModulesPath);
            value = value.Replace(RootPathMarker,     _serverOptions.ApplicationRootPath);
            value = value.Replace(PlatformMarker,     SystemInfo.Platform.ToLower());
            value = value.Replace(OSMarker,           SystemInfo.OperatingSystem.ToLower());
            value = value.Replace(PythonPathMarker,   _moduleOptions.PythonRelativeInterpreterPath);
            value = value.Replace(DataDirMarker,      _appDataDirectory);

            if (!string.IsNullOrEmpty(currentModulePath))
                value = value.Replace(CurrentModulePathMarker, currentModulePath);

            // Correct for cross platform (win = \, linux = /)
            value = Text.FixSlashes(value);

            return value;
        }
    }
}
