// The integration suite drives real hosts against a shared broker and uses process-wide static
// handler hooks for side-effect observation, so test classes must not run in parallel.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
