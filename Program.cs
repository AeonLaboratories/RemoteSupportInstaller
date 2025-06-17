// Installs and configures NetBird and RustDesk to provide Aeon Remote Desktop Support Service
//
// Before building, edit Program.Secrets.cs (copy from Program.Secrets.Template.cs) to
// match your configuration.
//
// Build a self contained version from the command line with "dotnet publish -c Release".
//

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

public static partial class Program
{
	static readonly string WintunVersion = "0.14.1";		// Update as needed

	static bool Enroll = true;

    static string NetbirdKey = "";
	static string NetbirdIP = "";
	static string RustdeskPassword = "";

    static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    static readonly string WindowsDrive = Path.GetPathRoot(WindowsDir)!;

    static async Task Main(string[] args)
    {
		Console.OutputEncoding = System.Text.Encoding.UTF8;
		EnsureRunningOnWindows();
		EnsureElevated();
        Console.WriteLine("=== Aeon Remote Support Installer ===");

        if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h" || args[0] == "-?" || args[0] == "--?"))
        {
            PrintUsage();
            return;
        }

        try
        {
            ParseArguments(args);
			if (NetbirdKey.StartsWith("<", StringComparison.Ordinal))
			{
				Console.WriteLine("❌ ERROR: Installer was built with placeholder secrets. Please update Program.Secrets.cs.");
				return;
			}
			
			await SetupNetBird();			
			await SetupRustDesk();
			if (Enroll)
				await Subscribe(NetbirdIP, RustdeskPassword);
			
            Console.WriteLine("✅ Aeon Remote Desktop Support Service is active.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ Error: {ex.Message}");
            Console.Error.WriteLine("Run 'RemoteSupportInstaller --help' for usage.");
            Environment.Exit(1);
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("\nUsage: RemoteSupportInstaller [options]\n");
        Console.WriteLine("Options:");
        Console.WriteLine("  --guest                  Don't subscribe to Aeon Remote Support");
        Console.WriteLine("  --netbird-key=<key>      Specify the NetBird setup key");
        Console.WriteLine("  --mgmt-url=<url>         Specify the NetBird management URL");
        Console.WriteLine("  --rustdesk-key=<key>     Specify the RustDesk server public key");
        Console.WriteLine("  --help, -h, --?          Show this help message\n");
    }	
	
    static void ParseArguments(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith("--guest"))
                Enroll = false;
            else if (arg.StartsWith("--netbird-key="))
                NetbirdKey = arg.Substring("--netbird-key=".Length);
            else if (arg.StartsWith("--mgmt-url="))
                MgmtUrl = arg.Substring("--mgmt-url=".Length);
            else if (arg.StartsWith("--rustdesk-key="))
                RustdeskKey = arg.Substring("--rustdesk-key=".Length);
        }
    }

	static async Task SetupNetBird()
	{
		string netbirdFolder = GetInstallFolder("Netbird");
		string netbirdExePath = Path.Combine(netbirdFolder, "netbird.exe");

		if (!File.Exists(netbirdExePath))
		{
			var localInstaller = Directory
				.EnumerateFiles(Directory.GetCurrentDirectory(), "netbird_installer_*.exe")
				.FirstOrDefault();

			if (localInstaller != null)
			{
				Console.WriteLine($"📁 Found local NetBird installer: {Path.GetFileName(localInstaller)}");
				Console.WriteLine("▶️ Launching installer...");
				RunCommand(localInstaller, "", ignoreError: false, verbose: true);
			}			
		}
		if (!File.Exists(netbirdExePath))
		{
			Console.WriteLine("📦 Installing NetBird");
			string netbirdUrl = await GetLatestNetbirdBinaryUrl();
			string archivePath = Path.Combine(Path.GetTempPath(), "netbird_win.tar.gz");
			await DownloadFileAsync(netbirdUrl, archivePath);

			InstallNetbirdBinary(archivePath, netbirdExePath);
			File.Delete(archivePath);

			await InstallWintun(netbirdFolder);
		}

		// Ensure netbird service is installed and running
		RunCommand(netbirdExePath, "service install", true);	// ignore error message if already installed
		RunCommand(netbirdExePath, "service start", true);		// ignore error message if already started
		Console.WriteLine("✅ NetBird service is running.");
		
		// Check agent status
		string status = GetNetbirdStatus(netbirdExePath);		
		bool connected = status.Contains("Management: Connected") && status.Contains("Signal: Connected");
		if (!connected)		// start netbird agent
		{
			Console.WriteLine("🔌 Connecting to VPN...");
			if (NetbirdKey == "")
				NetbirdKey = Enroll ? NetbirdSubscriberKey : NetbirdGuestKey;
			RunCommand(netbirdExePath, $"up --management-url \"{MgmtUrl}\" --setup-key \"{NetbirdKey}\"");
			Console.WriteLine("✅ NetBird agent started.");
			status = GetNetbirdStatus(netbirdExePath);
		}

		// Extract IP
        var match = Regex.Match(status, @"IP:\s*(\d+\.\d+\.\d+\.\d+)");
		if (match.Success)
		{
			NetbirdIP = match.Groups[1].Value;
			Console.WriteLine($"✅ Connected to Aeon Support VPN at {NetbirdIP}");
		}
		else
		{
			Console.WriteLine($"⚠️ Agent failed to connect to Aeon Support VPN");
		}
	}

    static async Task<string> GetLatestNetbirdBinaryUrl()
    {
        string versionApi = "https://api.github.com/repos/netbirdio/netbird/releases/latest";
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AeonRemoteSetup/1.0");
        var json = await client.GetStringAsync(versionApi);
        using var doc = JsonDocument.Parse(json);
        string version = (doc.RootElement.GetProperty("tag_name").GetString()!).TrimStart('v');
        return $"https://github.com/netbirdio/netbird/releases/download/v{version}/netbird_{version}_windows_amd64_signed.tar.gz";
    }

    static void InstallNetbirdBinary(string archivePath, string outputPath)
    {
        Console.WriteLine("Extracting NetBird binary...");
        string tempExtract = GetTempDir("netbird_extract");
        RunCommand("tar", $"-xzf \"{archivePath}\" -C \"{tempExtract}\"");
        string srcPath = Path.Combine(tempExtract, "netbird.exe");
        if (!File.Exists(srcPath))
            throw new Exception("netbird.exe not found in extracted archive.");
        File.Copy(srcPath, outputPath, overwrite: true);
        Directory.Delete(tempExtract, recursive: true);
    }
	
	static async Task InstallWintun(string destinationFolder)
	{
		string dllDestinationPath = Path.Combine(destinationFolder, "wintun.dll");
		if (File.Exists(dllDestinationPath)) return;
		
		Console.WriteLine("Installing Wintun driver...");

		// Define download URL and paths
		string zipUrl = $"https://www.wintun.net/builds/wintun-{WintunVersion}.zip";
		string tempZipPath = Path.Combine(Path.GetTempPath(), $"wintun-{WintunVersion}.zip");
		string extractPath = GetTempDir($"wintun-{WintunVersion}");

		await DownloadFileAsync(zipUrl, tempZipPath);
		ExtractZipArchive(tempZipPath, extractPath);

		// Locate the correct wintun.dll
		string arch = GetArchitectureString();
		string dllSourcePath = Path.Combine(extractPath, "wintun", "bin", arch, "wintun.dll");
		if (!File.Exists(dllSourcePath))
			throw new FileNotFoundException($"wintun.dll not found for architecture {arch}.");

		// Copy wintun.dll to the destination folder
		File.Copy(dllSourcePath, dllDestinationPath, overwrite: true);

		Console.WriteLine($"✅ Wintun driver installed to {dllDestinationPath}");
		
		// Clean up
		File.Delete(tempZipPath);
		Directory.Delete(extractPath, recursive: true);
	}

    static void StartNetbirdService(string netbirdExePath)
    {
        Console.WriteLine("Installing NetBird service...");
        RunCommand(netbirdExePath, "service install");
        Console.WriteLine("Starting NetBird service...");
        RunCommand(netbirdExePath, "service start");
    }

	static string GetNetbirdStatus(string netbirdExePath)
	{
        Console.WriteLine("Checking NetBird status...");
        var psi = new ProcessStartInfo(netbirdExePath, "status")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true
        };
        var proc = Process.Start(psi)!;
        string output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
		return output;
	}

	static async Task SetupRustDesk()
	{		
		string svcConfigDir = Path.Combine(WindowsDir, "ServiceProfiles", "LocalService", "AppData", "Roaming", "RustDesk");
		string appConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustDesk");
		string rustdeskFolder = GetInstallFolder("RustDesk");
		string rustdeskExe = Path.Combine(rustdeskFolder, "RustDesk.exe");

		bool found = false;

		if (Directory.Exists(svcConfigDir))
		{
			Console.WriteLine("⚠️ RustDesk service config found in:");
			Console.WriteLine($"   {svcConfigDir}");
			found = true;
		}

		if (Directory.Exists(appConfigDir))
		{
			Console.WriteLine("⚠️ RustDesk app config found in:");
			Console.WriteLine($"   {appConfigDir}");
			found = true;
		}
		
		if (File.Exists(rustdeskExe))
		{
			Console.WriteLine("⚠️ RustDesk app found in:");
			Console.WriteLine($"   {rustdeskFolder}");
			found = true;
		}

		if (found)
		{
			Console.WriteLine("\n❌ RustDesk appears to be partially installed or previously configured.");
			Console.WriteLine("   Please uninstall RustDesk with 'rustdesk --uninstall', then delete any config folders.");
			Console.WriteLine("   Then run this installer again.\n");
            Environment.Exit(1);
		}

		// Look for local RustDesk installer
		var localRustDesk = Directory
			.EnumerateFiles(Directory.GetCurrentDirectory(), "rustdesk-*.msi")
			.FirstOrDefault();

		string installerPath;

		if (localRustDesk != null)
		{
			Console.WriteLine($"📁 Found local RustDesk installer: {Path.GetFileName(localRustDesk)}");
			installerPath = localRustDesk;
		}
		else
		{
			Console.WriteLine("🌐 Downloading RustDesk...");
			string installerUrl = await GetLatestRustdeskDownloadUrl();
			installerPath = Path.Combine(Path.GetTempPath(), "rustdesk-installer.msi");
			await DownloadFileAsync(installerUrl, installerPath);
		}

		Console.WriteLine("Configuring RustDesk...");
		string configDir = Path.Combine(svcConfigDir, "config");
		string configFile = Path.Combine(configDir, "RustDesk.toml");
		string configFile2 = Path.Combine(configDir, "RustDesk2.toml");
		Directory.CreateDirectory(configDir);

		RustdeskPassword = GeneratePassword();
		string salt = GeneratePassword(6, 0);	// no uppers
		File.WriteAllText(configFile, $"""
password = '{RustdeskPassword}'
salt = '{salt}'
""");

		File.WriteAllText(configFile2, $"""
rendezvous_server = 'support.aeonhacs.vpn:21116'
nat_type = 1
serial = 0
unlock_pin = ''
trusted_devices = ''

[options]
local-ip-addr = '{NetbirdIP}'
custom-rendezvous-server = 'support.aeonhacs.vpn'
verification-method = 'use-both-passwords'
av1-test = 'Y'
relay-server = 'support.aeonhacs.vpn'
allow-remote-config-modification = 'Y'
enable-lan-discovery = 'N'
key = '{RustdeskKey}'
""");

		Console.WriteLine("Installing RustDesk...");
		RunCommand("msiexec", $"/i \"{installerPath}\" /qn INSTALLFOLDER=\"{rustdeskFolder}\" CREATESTARTMENUSHORTCUTS=\"Y\" CREATEDESKTOPSHORTCUTS=\"N\" INSTALLPRINTER=\"N\"");
		if (!installerPath.StartsWith(Directory.GetCurrentDirectory()))
			File.Delete(installerPath);
		
		Console.WriteLine($"✅ RustDesk service installed. Password is \'{RustdeskPassword}\'.");
	}

	static async Task<string> GetLatestRustdeskDownloadUrl()
	{
        string versionApi = "https://api.github.com/repos/rustdesk/rustdesk/releases/latest";
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AeonRemoteSetup/1.0");
        var json = await client.GetStringAsync(versionApi);
        using var doc = JsonDocument.Parse(json);		
        string version = (doc.RootElement.GetProperty("tag_name").GetString()!);
		return $"https://github.com/rustdesk/rustdesk/releases/download/{version}/rustdesk-{version}-x86_64.msi";
	}

	static string GetInstallFolder(string appName)
	{
		string currentDrive = Path.GetPathRoot(Directory.GetCurrentDirectory())!;
		string[] candidates =
		{
			Path.Combine(currentDrive, "Programs"),
			Path.Combine(WindowsDrive, "Programs"),
			Path.Combine(WindowsDrive, "Program Files")
		};

		// Prefer existing app-specific folder first
		foreach (var path in candidates)
		{
			var fullPath = Path.Combine(path, appName);
			if (Directory.Exists(fullPath))
				return fullPath;
		}

		// Fallback: pick first parent that exists, then append app name
		foreach (var path in candidates)
		{
			if (Directory.Exists(path))
			{
				var fullPath = Path.Combine(path, appName);
				Directory.CreateDirectory(fullPath);
				return fullPath;
			}
		}

		throw new DirectoryNotFoundException($"Can't find a suitable destination for {appName}.");
	}

	static string GetTempDir(string name)
	{
		string path = Path.Combine(Path.GetTempPath(), name);
		if (Directory.Exists(path))
			Directory.Delete(path, recursive: true);
		Directory.CreateDirectory(path);
		return path;
	}

	static string GeneratePassword(int length=8, int minUpper=1, int minLower=1, int minDigit=1)
    {
		Random rng = new Random();
        const string uppers = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lowers = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";

		// let the user suffer if they provide ridiculous numbers
		int minLength = minUpper + minLower + minDigit;
		if (length < minLength) length = minLength;
		
		char[] c = new char[length];		
		string all = "";
		int i = 0;
		
		if (minUpper > 0)
		{
			all += uppers;
			for (int j = 0; j < minUpper; j++)
				c[i++] = uppers[rng.Next(uppers.Length)];
		}
		
		if (minLower > 0)
		{
			all += lowers;
			for (int j = 0; j < minLower; j++)
				c[i++] = lowers[rng.Next(lowers.Length)];
		}
		
		if (minDigit > 0)
		{
			all += digits;
			for (int j = 0; j < minDigit; j++)
				c[i++] = digits[rng.Next(digits.Length)];
		}
		
		while (i < length)
			c[i++] = all[rng.Next(all.Length)];

		// Fisher-Yates shuffle
		for (int j = c.Length - 1; j > 0; j--)
		{
			int k = rng.Next(j + 1);
			(c[j], c[k]) = (c[k], c[j]);
		}

        return new string(c);
    }

    static async Task DownloadFileAsync(string url, string path)
    {
        Console.WriteLine($"Downloading: {url}");
        using var client = new HttpClient();
        using var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using var fs = new FileStream(path, FileMode.Create);
        await response.Content.CopyToAsync(fs);
    }

	static async Task Subscribe(string ip, string password)
	{
        Console.WriteLine($"Subscribing...");
		using var client = new HttpClient();
		var values = new Dictionary<string, string>
		{
			{ "ip", ip },
			{ "password", password }
		};
		var content = new FormUrlEncodedContent(values);
		try
		{
			var response = await client.PostAsync(ValetUrl, content);
			if (!response.IsSuccessStatusCode)
				Console.WriteLine($"⚠️ Valet subscription failed: {response.StatusCode}");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"⚠️ Valet subscription error: {ex.Message}");
		}
	}

	static void ExtractZipArchive(string zipPath, string extractTo)
	{
		Console.WriteLine($"Extracting archive to: {extractTo}");
		if (Directory.Exists(extractTo))
			Directory.Delete(extractTo, recursive: true);
		Directory.CreateDirectory(extractTo);
		System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractTo);
	}

    static void RunCommand(string file, string args, bool ignoreError = false, bool verbose = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var proc = Process.Start(psi)!;
		string output = proc.StandardOutput.ReadToEnd();
		string error = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
		var errorNoticed = !ignoreError && proc.ExitCode != 0;

		if (verbose || errorNoticed)
		{
			if (!string.IsNullOrWhiteSpace(output))
				Console.WriteLine(output);
			if (!string.IsNullOrWhiteSpace(error))
				Console.Error.WriteLine(error);
		}		
		
        if (errorNoticed)
        {
            throw new Exception($"'{file} {args}' failed with exit code {proc.ExitCode}.");
        }
     }

	static void AddToSystemPath(string folder)
	{
		const string envKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
		using var key = Registry.LocalMachine.OpenSubKey(envKey, writable: true);
		if (key == null)
			throw new InvalidOperationException("Failed to open system environment key.");

		string pathValue = (key.GetValue("Path") as string) ?? "";
		if (!pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries).Contains(folder, StringComparer.OrdinalIgnoreCase))
		{
			string newPath = pathValue.TrimEnd(';') + ";" + folder;
			key.SetValue("Path", newPath);
			Console.WriteLine($"✅ Added '{folder}' to system PATH.");
			BroadcastEnvironmentChange();
		}
		else
		{
			Console.WriteLine($"ℹ️ '{folder}' is already in system PATH.");
		}
	}

	// Notifies the system that environment variables have changed
	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
	private static extern int SendMessageTimeout(
		IntPtr hWnd, uint Msg, IntPtr wParam, string lParam,
		uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

	private static void BroadcastEnvironmentChange()
	{
		const int HWND_BROADCAST = 0xffff;
		const int WM_SETTINGCHANGE = 0x001A;
		const int SMTO_ABORTIFHUNG = 0x0002;

		SendMessageTimeout(
			(IntPtr)HWND_BROADCAST,
			WM_SETTINGCHANGE,
			IntPtr.Zero,
			"Environment",
			SMTO_ABORTIFHUNG,
			1000,
			out _);
	}

	static string GetArchitectureString()
	{
		return RuntimeInformation.ProcessArchitecture switch
		{
			Architecture.X64 => "amd64",
			Architecture.X86 => "x86",
			Architecture.Arm64 => "arm64",
			Architecture.Arm => "arm32",
			_ => throw new PlatformNotSupportedException("Unsupported architecture.")
		};
	}

	static void EnsureRunningOnWindows()
	{
		if (!OperatingSystem.IsWindows())
		{
			Console.Error.WriteLine("❌ This installer is only supported on Windows.");
			Environment.Exit(1);
		}
	}
	
	static void EnsureElevated()
	{
		using var identity = WindowsIdentity.GetCurrent();
		var principal = new WindowsPrincipal(identity);
		if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
		{
			Console.Error.WriteLine("❌ This installer must be run with administrative privileges.");
			Environment.Exit(1);
		}
	}
}
