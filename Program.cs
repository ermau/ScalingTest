using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;

namespace ScaleTest
{
	class Program
	{
		private const int TestCount = 3;
		private const int ElementsPerCore = 5000000;
		private const int WorkLoad = 1;
		private const bool UseLogicalCores = false;
		private const bool UseMaxOnly = true;

		static void Main (string[] args)
		{
			Console.WriteLine ("CPUs:");

			int totalCpus = 0;
			int totalPhysicalCores = 0;
			ulong totalMemory = 0;

			foreach (var ram in new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory").Get())
				totalMemory += UInt64.Parse (ram["Capacity"].ToString()) / 1024 / 1024;

			foreach (var cpu in new ManagementObjectSearcher ("SELECT * FROM Win32_Processor").Get())
			{
				totalCpus++;
				Console.WriteLine (cpu["Name"]);
				Console.WriteLine (cpu["Description"]);
				Console.WriteLine ("Physical cores: " + cpu["NumberOfCores"]);

				totalPhysicalCores += Int32.Parse (cpu["NumberOfCores"].ToString());

				Console.WriteLine();
			}

			Console.WriteLine ("Total CPUs: " + totalCpus);
			Console.WriteLine ("Total physical cores: " + totalPhysicalCores);
			Console.WriteLine ("Total logical cores: " + Environment.ProcessorCount);
			Console.WriteLine ("Total Memory: " + ((totalMemory > 1024) ? (totalMemory / 1024) + "GB" : totalMemory + "MB"));
			Console.WriteLine();

			int coresPerSide = ((UseLogicalCores) ? Environment.ProcessorCount : totalPhysicalCores) / 2;

			Console.WriteLine ("Using {0} for a total of {1} cores for processing (and {1} for clients)", (UseLogicalCores) ? "logical cores" : "physical cores", coresPerSide);
			Console.WriteLine ("Testing with {0:N0} elements with a work load of {1:N0} each", ElementsPerCore * coresPerSide, WorkLoad);
			Console.WriteLine();

			IProcessor<long>[] processors = new IProcessor<long>[]
			{
				new SingleThreadPollingProcessor<long>(),
				new SingleThreadSignaledProcessor<long>(),
				//new SingleThreadSlimSignaledProcessor<long>(),
				new SingleQueuePollingThreadPerCoreProcessor<long>(),
				new SingleQueueSignaledThreadPerCoreProcessor<long>(),
				//new SingleQueueSlimSignaledThreadPerCoreProcessor<long>(),
				new	RoundRobinQueueSignaledThreadPerCoreProcessor<long>()
			};

			StringBuilder builder = new StringBuilder();
			builder.Append ("{0,-25}");
			for (int t = 1; t < TestCount + 2; ++t)
			{
				builder.Append ("{");
				builder.Append (t.ToString());
				builder.Append (",-15}");
			}

			string format = builder.ToString();
			int elements = ElementsPerCore * coresPerSide;

			for (int i = 0; i < processors.Length; ++i)
			{
				IProcessor<long> proc = processors[i];

				for (int clients = ((!UseMaxOnly) ? 1 : coresPerSide); clients <= coresPerSide; ++clients)
				{
					int maxCores = ((proc.ScalesWithCores) ? coresPerSide : 1);
					for (int cores = ((!UseMaxOnly) ? 1 : maxCores); cores <= maxCores; ++cores)
					{
						List<Tuple<long, long>> times = new List<Tuple<long, long>>();

						for (int t = 0; t < TestCount + 1; ++t)
						{
							int counter = 1;
							long responseTime = 0;

							ManualResetEvent wait = new ManualResetEvent (false);
					
							proc.Start (time =>
							{
								Interlocked.Add (ref responseTime, Stopwatch.GetTimestamp() - time);

								// Busy work.
								int foo = 0;
								for (int x = 0; x < WorkLoad * 100; ++x)
									foo += x;

								if (Interlocked.Increment (ref counter) == elements)
									wait.Set();
							}, cores);

							ManualResetEvent clientWait = new ManualResetEvent (false);

							for (int c = 0; c < clients; ++c)
							{
								new Thread (() =>
								{
									clientWait.WaitOne();

									for (int e = 0; e < elements / clients; ++e)
										proc.Enqueue (Stopwatch.GetTimestamp());

								}).Start();
							}

							long start = Stopwatch.GetTimestamp();
							clientWait.Set();

							wait.WaitOne();
							long stop = Stopwatch.GetTimestamp();

							proc.Stop();
					
							if (t != 0)
							{
								times.Add (new Tuple<long, long> (stop - start, responseTime / elements));
								if (t == TestCount)
									processors[i] = null;
							}

							GC.Collect();
							GC.WaitForPendingFinalizers();
						}

						Console.WriteLine ("Test: {0} (Clients: {1}, Cores: {2})", proc.GetType().Name, clients, cores);//.Aggregate (String.Empty, (s,c) => s + ((Char.IsUpper (c) && s != String.Empty) ? " " + Char.ToLower (c) : c.ToString())).Substring (0, -2));
				
						Console.WriteLine (format, new[] { String.Empty }.Concat (Enumerable.Range (1, TestCount).Select (n => "Test " + n)).Concat (new[] { "Average" }).ToArray());

						var responseTimes = times.Select (t => TimeSpan.FromTicks (t.Item2).TotalMilliseconds).ToArray();
						var totalTimes = times.Select (t => TimeSpan.FromTicks (t.Item1).TotalMilliseconds).ToArray();

						Console.WriteLine (format, new [] { "Avg. response times:" }.Concat (responseTimes.Select (t => t.ToString ("N4") + "ms")).Concat (new object[] { responseTimes.Average().ToString ("N4") + "ms" }).ToArray());
						Console.WriteLine (format, new [] { "Total process time:" }.Concat (totalTimes.Select (t => t.ToString ("N4") + "ms")).Concat (new object[] { totalTimes.Average().ToString ("N4") + "ms" }).ToArray());
						Console.WriteLine();
					}
				}
			}
		}
	}

	public interface IProcessor<T>
	{
		bool ScalesWithCores { get; }

		void Enqueue (T element);
		void Start (Action<T> processor, int cores);
		void Stop();
	}
}