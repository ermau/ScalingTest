using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace ScaleTest
{
	public class SingleThreadPollingProcessor<T>
		: IProcessor<T>
	{
		public bool ScalesWithCores
		{
			get { return false; }
		}

		public void Enqueue (T element)
		{
			this.queue.Enqueue (element);
		}

		public void Start (Action<T> proc, int cores)
		{
			if (proc == null)
				throw new ArgumentNullException ("proc");

			this.running = true;
			this.processor = proc;
			this.processingThread = new Thread (Processor);
			this.processingThread.Start();
		}

		public void Stop()
		{
			if (!this.running)
				return;

			this.running = false;
			
			this.processingThread.Join();
			this.processingThread = null;
		}

		private volatile bool running;
		private readonly ConcurrentQueue<T> queue = new ConcurrentQueue<T>();
		private Thread processingThread;
		private Action<T> processor;

		private void Processor()
		{
			ConcurrentQueue<T> q = this.queue;
			Action<T> proc = this.processor;

			while (this.running)
			{
				T element;
				while (q.TryDequeue (out element))
					proc (element);
			}
		}
	}
}