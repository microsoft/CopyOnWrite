Benchmark numbers for Windows 22H2 in Dec 2022, using ReFS.

Machine was a physical 8/16-core with NVMe. Disable Write Cache Flush flag = Disabled (OS default). All copies done on files with size set via file extents (no data written).

16MB files are about 35X performance, 1MB at 3.2X, and small sizes at about 1.3X.

## Non-VHD, physical partition on disk on the NVMe disk

|    Method | FileSize |       Mean |     Error |    StdDev |     Median | Ratio | RatioSD |
|---------- |--------- |-----------:|----------:|----------:|-----------:|------:|--------:|
| File.Copy |        0 |   205.5 us |   8.82 us |  26.01 us |   200.1 us |  1.00 |    0.00 |
|       CoW |        0 |   183.7 us |   4.85 us |  14.16 us |   181.9 us |  0.91 |    0.13 |
|           |          |            |           |           |            |       |         |
| File.Copy |        1 |   307.9 us |   6.69 us |  19.53 us |   306.7 us |  1.00 |    0.00 |
|       CoW |        1 |   265.5 us |  15.18 us |  44.77 us |   265.6 us |  0.87 |    0.15 |
|           |          |            |           |           |            |       |         |
| File.Copy |     1024 |   310.3 us |   6.56 us |  18.82 us |   310.4 us |  1.00 |    0.00 |
|       CoW |     1024 |   235.1 us |   9.51 us |  26.82 us |   227.9 us |  0.76 |    0.10 |
|           |          |            |           |           |            |       |         |
| File.Copy |    16384 |   295.5 us |   5.80 us |  15.39 us |   295.2 us |  1.00 |    0.00 |
|       CoW |    16384 |   272.4 us |  16.46 us |  46.96 us |   275.9 us |  0.95 |    0.16 |
|           |          |            |           |           |            |       |         |
| File.Copy |   262144 |   425.5 us |   9.29 us |  26.80 us |   423.8 us |  1.00 |    0.00 |
|       CoW |   262144 |   234.6 us |   7.83 us |  22.96 us |   232.7 us |  0.55 |    0.06 |
|           |          |            |           |           |            |       |         |
| File.Copy |  1048576 |   851.7 us |  20.00 us |  56.08 us |   836.3 us |  1.00 |    0.00 |
|       CoW |  1048576 |   273.7 us |  15.39 us |  45.39 us |   269.9 us |  0.33 |    0.06 |
|           |          |            |           |           |            |       |         |
| File.Copy | 16777216 | 9,327.9 us | 179.64 us | 206.88 us | 9,282.0 us |  1.00 |    0.00 |
|       CoW | 16777216 |   273.3 us |  15.22 us |  44.17 us |   260.5 us |  0.04 |    0.00 |

## VHD formatted empty with ReFS for each iteration

|    Method | FileSize |       Mean |     Error |    StdDev |     Median | Ratio | RatioSD |
|---------- |--------- |-----------:|----------:|----------:|-----------:|------:|--------:|
| File.Copy |        0 |   202.8 us |   6.79 us |  19.92 us |   199.5 us |  1.00 |    0.00 |
|       CoW |        0 |   192.8 us |   5.40 us |  15.57 us |   191.2 us |  0.96 |    0.12 |
|           |          |            |           |           |            |       |         |
| File.Copy |        1 |   346.8 us |   9.65 us |  27.99 us |   345.8 us |  1.00 |    0.00 |
|       CoW |        1 |   237.5 us |   7.38 us |  21.28 us |   239.3 us |  0.69 |    0.08 |
|           |          |            |           |           |            |       |         |
| File.Copy |     1024 |   372.9 us |   9.75 us |  27.66 us |   371.2 us |  1.00 |    0.00 |
|       CoW |     1024 |   248.0 us |   6.97 us |  20.21 us |   248.5 us |  0.67 |    0.07 |
|           |          |            |           |           |            |       |         |
| File.Copy |    16384 |   347.1 us |  12.56 us |  35.62 us |   342.2 us |  1.00 |    0.00 |
|       CoW |    16384 |   248.8 us |   9.76 us |  28.47 us |   244.3 us |  0.72 |    0.11 |
|           |          |            |           |           |            |       |         |
| File.Copy |   262144 |   484.3 us |  11.83 us |  34.69 us |   482.4 us |  1.00 |    0.00 |
|       CoW |   262144 |   247.8 us |   8.51 us |  24.69 us |   245.6 us |  0.51 |    0.06 |
|           |          |            |           |           |            |       |         |
| File.Copy |  1048576 |   954.4 us |  19.06 us |  41.85 us |   948.0 us |  1.00 |    0.00 |
|       CoW |  1048576 |   251.3 us |   9.77 us |  28.64 us |   246.8 us |  0.27 |    0.03 |
|           |          |            |           |           |            |       |         |
| File.Copy | 16777216 | 9,867.2 us | 195.87 us | 546.01 us | 9,605.5 us |  1.00 |    0.00 |
|       CoW | 16777216 |   283.5 us |   6.56 us |  18.60 us |   282.7 us |  0.03 |    0.00 |
