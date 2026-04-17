using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Force all P/Invoke DLL lookups to search System32 only, blocking side-loading attacks.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

// InternalsVisibleTo without a public key is only safe when strong-naming is enabled.
// Once the assembly is strong-named, replace the value below with:
//   "WeekNumber.Tests, PublicKey=<hex-public-key>"
[assembly: InternalsVisibleTo("WeekNumber.Tests")]
