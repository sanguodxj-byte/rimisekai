using Xunit;

// 内容表（DefDatabase / DefLoader）是进程级全局状态，有的测试会清表、重载或改写。
// 测试类并行时彼此踩表，结果随调度时好时坏，所以整套串行跑（全套十来秒，不差这点）。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
