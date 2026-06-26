using Xunit;

// Each test class spins up its OWN real `func host` (one per feature-flag combination) but
// they all resolve to the SAME Logic App working directory and share the one local Azurite
// storage account. Running them in parallel would mean several stateful hosts contending on
// the same host id / storage partitions (and 7 func hosts competing for RAM/CPU), which is
// flaky. Run the classes sequentially so only one host is live at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
