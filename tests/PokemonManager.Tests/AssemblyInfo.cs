using Xunit;

// PKHeX's legality settings (ParseSettings) are global, so tests that check legality can't run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
