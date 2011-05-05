using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ScaleTest
{
	public class SingleQueueSlimSignaledThreadPerCoreProcessor<T>
		: IProcessor<T>
	{
		public bool ScalesWithCores
		{
			get { return true; }
		}

		public void Enqueue (T element)
		{
			this.queue.Enqueue (element);
			this.wait.Set();
		}

		public void Start (Action<T> proc, int cores)
		{
			if (proc == null)
				throw new ArgumentNullException ("proc");

			this.running = true;
			this.processor = proc;

			for (int i = 0; i < cores; ++i)
			{
				Thread processingThread = new Thread (Processor);
				processingThread.Start();

				this.processingThreads.Add (processingThread);
			}
		}

		public void Stop()
		{
			if (!this.running)
				return;

			this.running = false;
			this.wait.Set();

			while (this.processingThreads.Any (t => t.IsAlive))
				this.wait.Set();

			this.processingThreads.Clear();
		}

		private readonly ManualResetEventSlim wait = new ManualResetEventSlim (false);
		private volatile bool running;
		private readonly ConcurrentQueue<T> queue = new ConcurrentQueue<T>();
		private readonly List<Thread> processingThreads = new List<Thread>();
		private Action<T> processor;

		private void Processor ()
		{
			ManualResetEventSlim w = this.wait;
			ConcurrentQueue<T> q = this.queue;
			Action<T> proc = this.processor;

			while (this.running)
			{
				w.Wait();

				T element;
				while (q.TryDequeue (out element))
					proc (element);

				w.Reset();
			}
		}
	}
}
