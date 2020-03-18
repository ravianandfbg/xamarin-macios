/*
 * Copyright 2014 Xamarin Inc. All rights reserved.
 *
 * Authors:
 *   Rolf Bjarne Kvinge <rolf@xamarin.com>
 *
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.MacDev;
using Xamarin.Utils;
using ObjCRuntime;

namespace Xamarin.Bundler {
	public partial class Driver {
		static void AddSharedOptions (Application app, Mono.Options.OptionSet options)
		{
			options.Add ("sdkroot=", "Specify the location of Apple SDKs, default to 'xcode-select' value.", v => sdk_root = v);
			options.Add ("no-xcode-version-check", "Ignores the Xcode version check.", v => { min_xcode_version = null; }, true /* This is a non-documented option. Please discuss any customers running into the xcode version check on the maciosdev@ list before giving this option out to customers. */);
			options.Add ("warnaserror:", "An optional comma-separated list of warning codes that should be reported as errors (if no warnings are specified all warnings are reported as errors).", v =>
			{
				try {
					if (!string.IsNullOrEmpty (v)) {
						foreach (var code in v.Split (new char [] { ',' }, StringSplitOptions.RemoveEmptyEntries))
							ErrorHelper.SetWarningLevel (ErrorHelper.WarningLevel.Error, int.Parse (code));
					} else {
						ErrorHelper.SetWarningLevel (ErrorHelper.WarningLevel.Error);
					}
				} catch (Exception ex) {
					ErrorHelper.Error (26, ex, "Could not parse the command line argument '{0}': {1}", "--warnaserror", ex.Message);
				}
			});
			options.Add ("nowarn:", "An optional comma-separated list of warning codes to ignore (if no warnings are specified all warnings are ignored).", v =>
			{
				try {
					if (!string.IsNullOrEmpty (v)) {
						foreach (var code in v.Split (new char [] { ',' }, StringSplitOptions.RemoveEmptyEntries))
							ErrorHelper.SetWarningLevel (ErrorHelper.WarningLevel.Disable, int.Parse (code));
					} else {
						ErrorHelper.SetWarningLevel (ErrorHelper.WarningLevel.Disable);
					}
				} catch (Exception ex) {
					ErrorHelper.Error (26, ex, "Could not parse the command line argument '{0}': {1}", "--nowarn", ex.Message);
				}
			});
			options.Add ("coop:", "If the GC should run in cooperative mode.", v => { app.EnableCoopGC = ParseBool (v, "coop"); }, hidden: true);
			options.Add ("sgen-conc", "Enable the *experimental* concurrent garbage collector.", v => { app.EnableSGenConc = true; });
			options.Add ("marshal-objectivec-exceptions:", "Specify how Objective-C exceptions should be marshalled. Valid values: default, unwindmanagedcode, throwmanagedexception, abort and disable. The default depends on the target platform (on watchOS the default is 'throwmanagedexception', while on all other platforms it's 'disable').", v => {
				switch (v) {
				case "default":
					app.MarshalObjectiveCExceptions = MarshalObjectiveCExceptionMode.Default;
					break;
				case "unwindmanaged":
				case "unwindmanagedcode":
					app.MarshalObjectiveCExceptions = MarshalObjectiveCExceptionMode.UnwindManagedCode;
					break;
				case "throwmanaged":
				case "throwmanagedexception":
					app.MarshalObjectiveCExceptions = MarshalObjectiveCExceptionMode.ThrowManagedException;
					break;
				case "abort":
					app.MarshalObjectiveCExceptions = MarshalObjectiveCExceptionMode.Abort;
					break;
				case "disable":
					app.MarshalObjectiveCExceptions = MarshalObjectiveCExceptionMode.Disable;
					break;
				default:
					throw ErrorHelper.CreateError (26, "Could not parse the command line argument '{0}': {1}", "--marshal-objective-exceptions", $"Invalid value: {v}. Valid values are: default, unwindmanagedcode, throwmanagedexception, abort and disable.");
				}
			});
			options.Add ("marshal-managed-exceptions:", "Specify how managed exceptions should be marshalled. Valid values: default, unwindnativecode, throwobjectivecexception, abort and disable. The default depends on the target platform (on watchOS the default is 'throwobjectivecexception', while on all other platform it's 'disable').", v => {
				switch (v) {
				case "default":
					app.MarshalManagedExceptions = MarshalManagedExceptionMode.Default;
					break;
				case "unwindnative":
				case "unwindnativecode":
					app.MarshalManagedExceptions = MarshalManagedExceptionMode.UnwindNativeCode;
					break;
				case "throwobjectivec":
				case "throwobjectivecexception":
					app.MarshalManagedExceptions = MarshalManagedExceptionMode.ThrowObjectiveCException;
					break;
				case "abort":
					app.MarshalManagedExceptions = MarshalManagedExceptionMode.Abort;
					break;
				case "disable":
					app.MarshalManagedExceptions = MarshalManagedExceptionMode.Disable;
					break;
				default:
					throw ErrorHelper.CreateError (26, "Could not parse the command line argument '{0}': {1}", "--marshal-managed-exceptions", $"Invalid value: {v}. Valid values are: default, unwindnativecode, throwobjectivecexception, abort and disable.");
				}
			});
			options.Add ("j|jobs=", "The level of concurrency. Default is the number of processors.", v => {
				Jobs = int.Parse (v);
			});
			options.Add ("embeddinator", "Enables Embeddinator targetting mode.", v => {
				app.Embeddinator = true;
			}, true);
			options.Add ("dynamic-symbol-mode:", "Specify how dynamic symbols are treated so that they're not linked away by the native linker. Valid values: linker (pass \"-u symbol\" to the native linker), code (generate native code that uses the dynamic symbol), ignore (do nothing and hope for the best). The default is 'code' when using bitcode, and 'linker' otherwise.", (v) => {
				switch (v.ToLowerInvariant ()) {
				case "default":
					app.SymbolMode = SymbolMode.Default;
					break;
				case "linker":
					app.SymbolMode = SymbolMode.Linker;
					break;
				case "code":
					app.SymbolMode = SymbolMode.Code;
					break;
				case "ignore":
					app.SymbolMode = SymbolMode.Ignore;
					break;
				default:
					throw ErrorHelper.CreateError (26, "Could not parse the command line argument '{0}': {1}", "--dynamic-symbol-mode", $"Invalid value: {v}. Valid values are: default, linker, code and ignore.");
				}
			});
			options.Add ("ignore-dynamic-symbol:", "Specify that Xamarin.iOS/Xamarin.Mac should not try to prevent the linker from removing the specified symbol.", (v) => {
				app.IgnoredSymbols.Add (v);
			});
			options.Add ("root-assembly=", "Specifies any root assemblies. There must be at least one root assembly, usually the main executable.", (v) => {
				app.RootAssemblies.Add (v);
			});
			options.Add ("optimize=", "A comma-delimited list of optimizations to enable/disable. To enable an optimization, use --optimize=[+]remove-uithread-checks. To disable an optimizations: --optimize=-remove-uithread-checks. Use '+all' to enable or '-all' disable all optimizations. Only compiler-generated code or code otherwise marked as safe to optimize will be optimized.\n" +
					"Available optimizations:\n" +
					"    dead-code-elimination: By default always enabled (requires the linker). Removes IL instructions the linker can determine will never be executed. This is most useful in combination with the inline-* optimizations, since inlined conditions almost always also results in blocks of code that will never be executed.\n" +
					"    remove-uithread-checks: By default enabled for release builds (requires the linker). Remove all UI Thread checks (makes the app smaller, and slightly faster at runtime).\n" +
#if MONOTOUCH
					"    inline-isdirectbinding: By default enabled unless the interpreter is enabled (requires the linker). Tries to inline calls to NSObject.IsDirectBinding to load a constant value. Makes the app smaller, and slightly faster at runtime.\n" +
#else
					"    inline-isdirectbinding: By default disabled, because it may require the linker. Tries to inline calls to NSObject.IsDirectBinding to load a constant value. Makes the app smaller, and slightly faster at runtime.\n" +
#endif
#if MONOTOUCH
					"    remove-dynamic-registrar: By default enabled when the static registrar is enabled and the interpreter is not used. Removes the dynamic registrar (makes the app smaller).\n" +
					"    inline-runtime-arch: By default always enabled (requires the linker). Inlines calls to ObjCRuntime.Runtime.Arch to load a constant value. Makes the app smaller, and slightly faster at runtime.\n" +
#endif
					"    blockliteral-setupblock: By default enabled when using the static registrar. Optimizes calls to BlockLiteral.SetupBlock to avoid having to calculate the block signature at runtime.\n" +
					"    inline-intptr-size: By default enabled for builds that target a single architecture (requires the linker). Inlines calls to IntPtr.Size to load a constant value. Makes the app smaller, and slightly faster at runtime.\n" +
					"    inline-dynamic-registration-supported: By default always enabled (requires the linker). Optimizes calls to Runtime.DynamicRegistrationSupported to be a constant value. Makes the app smaller, and slightly faster at runtime.\n" +
#if !MONOTOUCH
					"    register-protocols: Remove unneeded metadata for protocol support. Makes the app smaller and reduces memory requirements. Disabled when the interpreter is used or when the static registrar is not enabled.\n" +
					"    trim-architectures: Remove unneeded architectures from bundled native libraries. Makes the app smaller and is required for macOS App Store submissions.\n" +
#else
					"    register-protocols: Remove unneeded metadata for protocol support. Makes the app smaller and reduces memory requirements. Disabled, by default, to allow dynamic code loading.\n" +
					"    remove-unsupported-il-for-bitcode: Remove IL that is not supported when compiling to bitcode, and replace with a NotSupportedException.\n" +
					"    force-rejected-types-removal: Forcefully remove types that are known to cause rejections when applications are submitted to Apple. This includes: `UIWebView` and related types.\n" +
#endif
					"",
					(v) => {
						app.Optimizations.Parse (v);
					});
			options.Add (new Mono.Options.ResponseFileSource ());
		}

		static int Jobs;
		public static int Concurrency {
			get {
				return Jobs == 0 ? Environment.ProcessorCount : Jobs;
			}
		}

		public static int Verbosity {
			get { return verbose; }
		}

		public const bool IsXAMCORE_4_0 = false;

#if MONOMAC
#pragma warning disable 0414
		static string userTargetFramework = TargetFramework.Default.ToString ();
#pragma warning restore 0414
#endif

		static TargetFramework? targetFramework;
		public static bool HasTargetFramework {
			get { return targetFramework.HasValue; }
		}

		public static TargetFramework TargetFramework {
			get { return targetFramework.Value; }
			set { targetFramework = value; }
		}

		static void SetTargetFramework (string fx)
		{
#if MONOMAC
			userTargetFramework = fx;
#endif

			switch (fx.Trim ().ToLowerInvariant ()) {
#if MONOMAC
			case "xammac":
			case "mobile":
			case "xamarin.mac":
				targetFramework = TargetFramework.Xamarin_Mac_2_0;
				break;
#endif
			default:
				TargetFramework parsedFramework;
				if (!Xamarin.Utils.TargetFramework.TryParse (fx, out parsedFramework))
					throw ErrorHelper.CreateError (68, "Invalid value for target framework: {0}.", fx);
#if MONOMAC
				if (parsedFramework == TargetFramework.Net_3_0 || parsedFramework == TargetFramework.Net_3_5)
					parsedFramework = TargetFramework.Net_2_0;
#endif

				targetFramework = parsedFramework;

				break;
			}

#if MTOUCH
			if (Array.IndexOf (TargetFramework.ValidFrameworks, targetFramework.Value) == -1)
				throw ErrorHelper.CreateError (70, "Invalid target framework: {0}. Valid target frameworks are: {1}.", targetFramework.Value, string.Join (" ", TargetFramework.ValidFrameworks.Select ((v) => v.ToString ()).ToArray ()));
#endif
		}

		public static int RunCommand (string path, params string [] args)
		{
			return RunCommand (path, args, null, (Action<string>) null);
		}

		public static int RunCommand (string path, IList<string> args)
		{
			return RunCommand (path, args, null, (Action<string>) null);
		}

		public static int RunCommand (string path, IList<string> args, string [] env = null, StringBuilder output = null, bool suppressPrintOnErrors = false)
		{
			if (output != null)
				return RunCommand (path, args, env, (v) => { if (v != null) output.AppendLine (v); }, suppressPrintOnErrors);
			return RunCommand (path, args, env, (Action<string>) null, suppressPrintOnErrors);
		}

		public static int RunCommand (string path, IList<string> args, string [] env = null, Action<string> output_received = null, bool suppressPrintOnErrors = false)
		{
			return RunCommand (path, StringUtils.FormatArguments (args), env, output_received, suppressPrintOnErrors);
		}

		static int RunCommand (string path, string args, string[] env = null, Action<string> output_received = null, bool suppressPrintOnErrors = false)
		{
			Exception stdin_exc = null;
			var info = new ProcessStartInfo (path, args);
			info.UseShellExecute = false;
			info.RedirectStandardInput = false;
			info.RedirectStandardOutput = true;
			info.RedirectStandardError = true;
			System.Threading.ManualResetEvent stdout_completed = new System.Threading.ManualResetEvent (false);
			System.Threading.ManualResetEvent stderr_completed = new System.Threading.ManualResetEvent (false);

			var lockobj = new object ();
			StringBuilder output = null;
			if (output_received == null) {
				output = new StringBuilder ();
				output_received = (line) => {
					if (line != null)
						output.AppendLine (line);
				};
			}

			if (env != null){
				if (env.Length % 2 != 0)
					throw new Exception ("You passed an environment key without a value");

				for (int i = 0; i < env.Length; i += 2) {
					if (env [i + 1] == null) {
						info.EnvironmentVariables.Remove (env [i]);
					} else {
						info.EnvironmentVariables [env [i]] = env [i + 1];
					}
				}
			}

			if (verbose > 0)
				Console.WriteLine ("{0} {1}", path, args);

			using (var p = Process.Start (info)) {

				p.OutputDataReceived += (s, e) => {
					if (e.Data != null) {
						lock (lockobj)
							output_received (e.Data);
					} else {
						stdout_completed.Set ();
					}
				};

				p.ErrorDataReceived += (s, e) => {
					if (e.Data != null) {
						lock (lockobj)
							output_received (e.Data);
					} else {
						stderr_completed.Set ();
					}
				};

				p.BeginOutputReadLine ();
				p.BeginErrorReadLine ();

				p.WaitForExit ();

				stderr_completed.WaitOne (TimeSpan.FromSeconds (1));
				stdout_completed.WaitOne (TimeSpan.FromSeconds (1));

				output_received (null);

				if (p.ExitCode != 0) {
					// note: this repeat the failing command line. However we can't avoid this since we're often
					// running commands in parallel (so the last one printed might not be the one failing)
					if (!suppressPrintOnErrors) {
						// We re-use the stringbuilder so that we avoid duplicating the amount of required memory,
						// while only calling Console.WriteLine once to make it less probable that other threads
						// also write to the Console, confusing the output.
						if (output == null)
							output = new StringBuilder ();
						output.Insert (0, $"Process exited with code {p.ExitCode}, command:\n{path} {args}\n");
						Console.Error.WriteLine (output);
					}
					return p.ExitCode;
				} else if (verbose > 0 && output != null && output.Length > 0 && !suppressPrintOnErrors) {
					Console.WriteLine (output.ToString ());
				}

				if (stdin_exc != null)
					throw stdin_exc;
			}

			return 0;
		}

		public static Task<int> RunCommandAsync (string path, string[] args, string [] env = null, StringBuilder output = null, bool suppressPrintOnErrors = false)
		{
			if (output != null)
				return RunCommandAsync (path, args, env, (v) => { if (v != null) output.AppendLine (v); }, suppressPrintOnErrors);
			return RunCommandAsync (path, args, env, (Action<string>) null, suppressPrintOnErrors);
		}

		public static Task<int> RunCommandAsync (string path, string[] args, string [] env = null, Action<string> output_received = null, bool suppressPrintOnErrors = false)
		{
			return Task.Run (() => RunCommand (path, args, env, output_received, suppressPrintOnErrors));
		}

#if !MMP_TEST
		static void FileMove (string source, string target)
		{
			Application.TryDelete (target);
			File.Move (source, target);
		}

		static void MoveIfDifferent (string path, string tmp, bool use_stamp = false)
		{
			// Don't read the entire file into memory, it can be quite big in certain cases.

			bool move = false;

			using (var fs1 = new FileStream (path, FileMode.Open, FileAccess.Read)) {
				using (var fs2 = new FileStream (tmp, FileMode.Open, FileAccess.Read)) {
					if (fs1.Length != fs2.Length) {
						Log (3, "New file '{0}' has different length, writing new file.", path);
						move = true;
					} else {
						move = !Cache.CompareStreams (fs1, fs2);
					}
				}
			}

			if (move) {
				FileMove (tmp, path);
			} else {
				Log (3, "Target {0} is up-to-date.", path);
				if (use_stamp)
					Driver.Touch (path + ".stamp");
			}
		}

		public static void WriteIfDifferent (string path, string contents, bool use_stamp = false)
		{
			var tmp = path + ".tmp";

			try {
				if (!File.Exists (path)) {
					Directory.CreateDirectory (Path.GetDirectoryName (path));
					File.WriteAllText (path, contents);
					Log (3, "File '{0}' does not exist, creating it.", path);
					return;
				}

				File.WriteAllText (tmp, contents);
				MoveIfDifferent (path, tmp, use_stamp);
			} catch (Exception e) {
				File.WriteAllText (path, contents);
				ErrorHelper.Warning (1014, e, "Failed to re-use cached version of '{0}': {1}.", path, e.Message);
			} finally {
				Application.TryDelete (tmp);
			}
		}

		public static void WriteIfDifferent (string path, byte[] contents, bool use_stamp = false)
		{
			var tmp = path + ".tmp";

			try {
				if (!File.Exists (path)) {
					File.WriteAllBytes (path, contents);
					Log (3, "File '{0}' does not exist, creating it.", path);
					return;
				}

				File.WriteAllBytes (tmp, contents);
				MoveIfDifferent (path, tmp, use_stamp);
			} catch (Exception e) {
				File.WriteAllBytes (path, contents);
				ErrorHelper.Warning (1014, e, "Failed to re-use cached version of '{0}': {1}.", path, e.Message);
			} finally {
				Application.TryDelete (tmp);
			}
		}
#endif


		internal static string GetFullPath ()
		{
			return System.Reflection.Assembly.GetExecutingAssembly ().Location;
		}

		static string xcode_product_version;
		public static string XcodeProductVersion {
			get {
				return xcode_product_version;
			}
		}

		static Version xcode_version;
		public static Version XcodeVersion {
			get {
				return xcode_version;
			}
		}

		static void SetCurrentLanguage ()
		{
			// There's no way to change the current culture from the command-line
			// without changing the system settings, so honor LANG if set.
			// This eases testing mtouch/mmp with different locales significantly,
			// and won't run into issues where changing the system language leaves
			// the tester with an incomprehensible system.
			var lang_variable = Environment.GetEnvironmentVariable ("LANG");
			if (string.IsNullOrEmpty (lang_variable))
				return;

			// Mimic how mono transforms LANG into a culture name:
			// https://github.com/mono/mono/blob/fc6e8a27fc55319141ceb29fbb7b5c63a9030b5e/mono/metadata/locales.c#L568-L576
			var lang = lang_variable;
			var idx = lang.IndexOf ('.');
			if (idx >= 0)
				lang = lang.Substring (0, idx);
			idx = lang.IndexOf ('@');
			if (idx >= 0)
				lang = lang.Substring (0, idx);
			lang = lang.Replace ('_', '-');
			try {
				var culture = CultureInfo.GetCultureInfo (lang);
				if (culture != null) {
					CultureInfo.DefaultThreadCurrentCulture = culture;
					Log (2, $"The current language was set to '{culture.DisplayName}' according to the LANG environment variable (LANG={lang_variable}).");
				}
			} catch (Exception e) {
				ErrorHelper.Warning (124, e, $"Could not set the current language to '{lang}' (according to LANG={lang_variable}): {e.Message}");
			}
		}

		static void LogArguments (string [] arguments)
		{
			if (Verbosity < 1)
				return;
			if (!arguments.Any ((v) => v.Length > 0 && v [0] == '@'))
				return; // no need to print arguments unless we get response files
			LogArguments (arguments, 1);
		}

		static void LogArguments (string [] arguments, int indentation)
		{
			Log ("Provided arguments:");
			var indent = new string (' ', indentation * 4);
			foreach (var arg in arguments) {
				Log (indent + StringUtils.Quote (arg));
				if (arg.Length > 0 && arg [0] == '@') {
					var fn = arg.Substring (1);
					LogArguments (File.ReadAllLines (fn), indentation + 1);
				}
			}
		}

		public static void Touch (IEnumerable<string> filenames, DateTime? timestamp = null)
		{
			if (timestamp == null)
				timestamp = DateTime.Now;
			foreach (var filename in filenames) {
				try {
					var fi = new FileInfo (filename);
					if (!fi.Exists) {
						using (var fo = fi.OpenWrite ()) {
							// Create an empty file.
						}
					}
					fi.LastWriteTime = timestamp.Value;
				} catch (Exception e) {
					ErrorHelper.Warning (128, "Could not touch the file '{0}': {1}", filename, e.Message);
				}
			}
		}

		public static void Touch (params string [] filenames)
		{
			Touch ((IEnumerable<string>) filenames);
		}

		static int watch_level;
		static Stopwatch watch;

		public static int WatchLevel {
			get { return watch_level; }
			set {
				watch_level = value;
				if ((watch_level > 0) && (watch == null)) {
					watch = new Stopwatch ();
					watch.Start ();
				}
			}
		}

		public static void Watch (string msg, int level)
		{
			if ((watch == null) || (level > WatchLevel))
				return;
			for (int i = 0; i < level; i++)
				Console.Write ("!");
			Console.WriteLine ("Timestamp {0}: {1} ms", msg, watch.ElapsedMilliseconds);
		}

		internal static PDictionary FromPList (string name)
		{
			if (!File.Exists (name))
				throw ErrorHelper.CreateError (24, "Could not find required file '{0}'.", name);
			return PDictionary.FromFile (name);
		}

		const string XcodeDefault = "/Applications/Xcode.app";

		static string FindSystemXcode ()
		{
			var output = new StringBuilder ();
			if (Driver.RunCommand ("xcode-select", new [] { "-p" }, output: output) != 0) {
				ErrorHelper.Warning (59, "Could not find the currently selected Xcode on the system: {0}", output.ToString ());
				return null;
			}
			return output.ToString ().Trim ();
		}

		static string sdk_root;
		static string developer_directory;

		public static string DeveloperDirectory {
			get {
				return developer_directory;
			}
		}

		static void ValidateXcode (bool accept_any_xcode_version, bool warn_if_not_found)
		{
			if (sdk_root == null) {
				sdk_root = FindSystemXcode ();
				if (sdk_root == null) {
					// FindSystemXcode showed a warning in this case. In particular do not use 'string.IsNullOrEmpty' here,
					// because FindSystemXcode may return an empty string (with no warning printed) if the xcode-select command
					// succeeds, but returns nothing.
					sdk_root = null;
				} else if (!Directory.Exists (sdk_root)) {
					ErrorHelper.Warning (60, "Could not find the currently selected Xcode on the system. 'xcode-select --print-path' returned '{0}', but that directory does not exist.", sdk_root);
					sdk_root = null;
				} else {
					if (!accept_any_xcode_version)
						ErrorHelper.Warning (61, "No Xcode.app specified (using --sdkroot), using the system Xcode as reported by 'xcode-select --print-path': {0}", sdk_root);
				}
				if (sdk_root == null) {
					sdk_root = XcodeDefault;
					if (!Directory.Exists (sdk_root)) {
						if (warn_if_not_found) {
							// mmp: and now we give up, but don't throw like mtouch, because we don't want to change behavior (this sometimes worked it appears)
							ErrorHelper.Warning (56, "Cannot find Xcode in any of our default locations. Please install Xcode, or pass a custom path using --sdkroot=<path>.");
							return; // Can't validate the version below if we can't even find Xcode...
						}

						throw ErrorHelper.CreateError (56, "Cannot find Xcode in the default location (/Applications/Xcode.app). Please install Xcode, or pass a custom path using --sdkroot <path>.");
					}
					ErrorHelper.Warning (62, "No Xcode.app specified (using --sdkroot or 'xcode-select --print-path'), using the default Xcode instead: {0}", sdk_root);
				}
			} else if (!Directory.Exists (sdk_root)) {
				throw ErrorHelper.CreateError (55, "The Xcode path '{0}' does not exist.", sdk_root);
			}

			// Check what kind of path we got
			if (File.Exists (Path.Combine (sdk_root, "Contents", "MacOS", "Xcode"))) {
				// path to the Xcode.app
				developer_directory = Path.Combine (sdk_root, "Contents", "Developer");
			} else if (File.Exists (Path.Combine (sdk_root, "..", "MacOS", "Xcode"))) {
				// path to Contents/Developer
				developer_directory = Path.GetFullPath (Path.Combine (sdk_root, "..", "..", "Contents", "Developer"));
			} else {
				throw ErrorHelper.CreateError (57, "Cannot determine the path to Xcode.app from the sdk root '{0}'. Please specify the full path to the Xcode.app bundle.", sdk_root);
			}
			
			var plist_path = Path.Combine (Path.GetDirectoryName (DeveloperDirectory), "version.plist");

			if (File.Exists (plist_path)) {
				var plist = FromPList (plist_path);
				var version = plist.GetString ("CFBundleShortVersionString");
				xcode_version = new Version (version);
				xcode_product_version = plist.GetString ("ProductBuildVersion");
			} else {
				throw ErrorHelper.CreateError (58, "The Xcode.app '{0}' is invalid (the file '{1}' does not exist).", Path.GetDirectoryName (Path.GetDirectoryName (DeveloperDirectory)), plist_path);
			}

			if (!accept_any_xcode_version) {
				if (min_xcode_version != null && XcodeVersion < min_xcode_version)
					throw ErrorHelper.CreateError (51, "{3} {0} requires Xcode {4} or later. The current Xcode version (found in {2}) is {1}.", Constants.Version, XcodeVersion.ToString (), sdk_root, PRODUCT, min_xcode_version);

				if (XcodeVersion < SdkVersions.XcodeVersion)
					ErrorHelper.Warning (79, "The recommended Xcode version for {4} {0} is Xcode {3} or later. The current Xcode version (found in {2}) is {1}.", Constants.Version, XcodeVersion.ToString (), sdk_root, SdkVersions.Xcode, PRODUCT);
			}

			Driver.Log (1, "Using Xcode {0} ({2}) found in {1}", XcodeVersion, sdk_root, XcodeProductVersion);
		}

		internal static bool TryParseBool (string value, out bool result)
		{
			if (string.IsNullOrEmpty (value)) {
				result = true;
				return true;
			}

			switch (value.ToLowerInvariant ()) {
			case "1":
			case "yes":
			case "true":
			case "enable":
				result = true;
				return true;
			case "0":
			case "no":
			case "false":
			case "disable":
				result = false;
				return true;
			default:
				return bool.TryParse (value, out result);
			}
		}

		internal static bool ParseBool (string value, string name, bool show_error = true)
		{
			bool result;
			if (!TryParseBool (value, out result))
				throw ErrorHelper.CreateError (26, "Could not parse the command line argument '-{0}': {1}", name, value);
			return result;
		}

		static readonly Dictionary<string, string> tools = new Dictionary<string, string> ();
		static string FindTool (string tool)
		{
			string path;

			lock (tools) {
				if (tools.TryGetValue (tool, out path))
					return path;
			}

			path = LocateTool (tool);
			static string LocateTool (string tool)
			{
				if (XcrunFind (tool, out var path))
					return path;

				// either /Developer (Xcode 4.2 and earlier), /Applications/Xcode.app/Contents/Developer (Xcode 4.3) or user override
				path = Path.Combine (DeveloperDirectory, "usr", "bin", tool);
				if (File.Exists (path))
					return path;

				// Xcode 4.3 (without command-line tools) also has a copy of 'strip'
				path = Path.Combine (DeveloperDirectory, "Toolchains", "XcodeDefault.xctoolchain", "usr", "bin", tool);
				if (File.Exists (path))
					return path;

				// Xcode "Command-Line Tools" install a copy in /usr/bin (and it can be there afterward)
				path = Path.Combine ("/usr", "bin", tool);
				if (File.Exists (path))
					return path;

				return null;
			}

			// We can end up finding the same tool multiple times.
			// That's not a problem.
			lock (tools)
				tools [tool] = path;

			if (path == null)
				throw ErrorHelper.CreateError (5307, "Missing '{0}' tool. Please install Xcode 'Command-Line Tools' component", tool);

			return path;
		}

		static bool XcrunFind (string tool, out string path)
		{
			return XcrunFind (ApplePlatform.None, false, tool, out path);
		}

		static bool XcrunFind (ApplePlatform platform, bool is_simulator, string tool, out string path)
		{
			var env = new List<string> ();
			// Unset XCODE_DEVELOPER_DIR_PATH. See https://github.com/xamarin/xamarin-macios/issues/3931.
			env.Add ("XCODE_DEVELOPER_DIR_PATH");
			env.Add (null);
			// Set DEVELOPER_DIR if we have it
			if (!string.IsNullOrEmpty (DeveloperDirectory)) {
				env.Add ("DEVELOPER_DIR");
				env.Add (DeveloperDirectory);
			}

			path = null;

			var args = new List<string> ();
			if (platform != ApplePlatform.None) {
				args.Add ("-sdk");
				switch (platform) {
				case ApplePlatform.iOS:
					args.Add (is_simulator ? "iphonesimulator" : "iphoneos");
					break;
				case ApplePlatform.MacOSX:
					args.Add ("macosx");
					break;
				case ApplePlatform.TVOS:
					args.Add (is_simulator ? "appletvsimulator" : "appletvos");
					break;
				case ApplePlatform.WatchOS:
					args.Add (is_simulator ? "watchsimulator" : "watchos");
					break;
				default:
					throw ErrorHelper.CreateError (71, "Unknown platform: {0}. This usually indicates a bug in {1}; please file a bug report at https://github.com/xamarin/xamarin-macios/issues/new with a test case.", platform.ToString (), PRODUCT);
				}
			}
			args.Add ("-f");
			args.Add (tool);

			var output = new StringBuilder ();
			int ret = RunCommand ("xcrun", args, env.ToArray (), output);

			if (ret == 0) {
				path = output.ToString ().Trim ();
			} else {
				Log (1, "Failed to locate the developer tool '{0}', 'xcrun {1}' returned with the exit code {2}:\n{3}", tool, string.Join (" ", args), ret, output.ToString ());
			}

			return ret == 0;
		}

		public static void RunXcodeTool (string tool, params string[] arguments)
		{
			RunXcodeTool (tool, (IList<string>) arguments);
		}

		public static void RunXcodeTool (string tool, IList<string> arguments)
		{
			var executable = FindTool (tool);
			var rv = RunCommand (executable, arguments);
			if (rv != 0)
				throw ErrorHelper.CreateError (5309, "Failed to execute the tool '{0}', it failed with an error code '{1}'. Please check the build log for details.", tool, rv);
		}

		public static void RunClang (IList<string> arguments)
		{
			RunXcodeTool ("clang", arguments);
		}

		public static void RunInstallNameTool (IList<string> arguments)
		{
			RunXcodeTool ("install_name_tool", arguments);
		}

		public static void RunBitcodeStrip (IList<string> arguments)
		{
			RunXcodeTool ("bitcode_strip", arguments);
		}

		public static void RunLipo (string output, IEnumerable<string> inputs)
		{
			var sb = new List<string> ();
			sb.AddRange (inputs);
			sb.Add ("-create");
			sb.Add ("-output");
			sb.Add (output);
			RunLipo (sb);
		}

		public static void RunLipo (IList<string> options)
		{
			RunXcodeTool ("lipo", options);
		}

		public static void CreateDsym (string output_dir, string appname, string dsym_dir)
		{
			RunDsymUtil (Path.Combine (output_dir, appname), "-num-threads", "4", "-z", "-o", dsym_dir);
			RunCommand ("/usr/bin/mdimport", dsym_dir);
		}

		public static void RunDsymUtil (params string [] options)
		{
			RunXcodeTool ("dsymutil", options);
		}

		public static void RunStrip (IList<string> options)
		{
			RunXcodeTool ("strip", options);
		}
	}
}
