using Xunit;

// Les statiques d'isolation (Settings.DirOverride, AppService.SkipRunKey, WallpaperCache.Disabled,
// Log.Enabled) sont globales au process : deux classes de tests en parallèle se piétineraient.
// Les tests sont assez rapides (< 2 s) pour que la séquentialité soit sans coût.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
