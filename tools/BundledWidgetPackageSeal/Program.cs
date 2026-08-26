using WidgetRail.WidgetCatalog;

if (args.Length != 2) { Console.Error.WriteLine("Usage: BundledWidgetPackageSeal <catalog-root> <package-root>"); return 2; }
try { Console.WriteLine($"Sealed bundled widget package {InstalledPackageIntegrity.Seal(args[0], args[1], new WidgetCatalogOptions())}"); return 0; }
catch (Exception exception) when (exception is not OutOfMemoryException) { Console.Error.WriteLine($"Bundled widget package sealing failed: {exception.Message}"); return 1; }
