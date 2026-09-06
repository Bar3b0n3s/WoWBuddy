using Xunit;

// The logger is process-wide static state, and LogSinkTests tears it down and rebuilds it to
// check that an extra sink is wired in. Any other test writing a log line while that happens
// races with it — which is exactly what made the suite fail intermittently when the whole
// solution ran but pass when this project ran alone. Serialising this one small assembly is
// cheaper than making the logger injectable everywhere for the sake of a single test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
