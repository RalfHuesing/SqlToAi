# German STATISTICS messages parse as zero

Order: 2. Do not change the `sql_execute_query` success-line shape. [04](04-write-result-looks-like-a-read.md) does that later.

## Problem

`PerformanceMetricsCalculator.ParseRunMessages` matches English text only:

- `CPU time = (\d+) ms,\s+elapsed time = (\d+) ms`
- `logical reads (\d+),\s+physical reads (\d+),\s+read-ahead reads (\d+)`

This instance is `Deutsch`, so the messages are `CPU-Zeit` and `logische Lesevorgänge`. CPU and logical reads stay 0 in `sql_execute_query` and in `sql_measure_performance`. Elapsed time on `sql_execute_query` comes from a stopwatch and is already fine.

`HasMatch` is set only when the CPU pattern matches. A message that matches only the IO pattern still adds the read counts, but multi-run min/max treats the run as unmatched.

## Required outcome

Add these patterns beside the English ones:

- `CPU-Zeit = (\d+) ms,\s+verstrichene Zeit = (\d+) ms`
- `logische Lesevorgänge (\d+),\s+physische Lesevorgänge (\d+),\s+Read-Ahead-Lesevorgänge (\d+)`

Sum every match, including a compile line and an execution line. If one message matches both languages, count it once. Ignore LOB counters.

`HasMatch` is true when a message matches the CPU pattern or the IO pattern of either language.

Do not `SET LANGUAGE`. Do not add an unparsed flag. Do not fail `sql_measure_performance` when text is unrecognized. Do not change `OptimizationBenchmarkService`.

## Tests

Calculator fixtures only. No German SQL Server.

- Existing English fixtures keep their totals.
- `CPU-Zeit = 15 ms, verstrichene Zeit = 12 ms.` plus `logische Lesevorgänge 3, physische Lesevorgänge 0, Read-Ahead-Lesevorgänge 0` yields cpu 15, elapsed 12, logical 3, physical 0, read-ahead 0, `HasMatch=true`.
- An IO-only message, with no CPU line, sets `HasMatch=true` and the logical-read count from that message.

## Out of scope

- Query Store, DMVs, and translated tool text.
- The stopwatch elapsed time on `sql_execute_query`.
