using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace ScaleTest
{
	public class RoundRobinQueueSignaledThreadPerCoreProcessor<T>
		: IProcessor<T>
	{
		public bool ScalesWithCores
		{
			get { return true; }
		}

		public void Enqueue (T element)
		{
			int next = Interlocked.Increment (ref this.nextQueue);
			next = next % this.queues.Length;
			//int next, set;
			//do
			//{
			//    next = this.nextQueue;
			//    set = (next + 1 < this.queues.Length) ? next + 1 : 0;
			//} while (Interlocked.CompareExchange (ref this.nextQueue, set, next) != next);

			var t = this.queues[next];
			t.Item1.Enqueue (element);
			t.Item2.Set();
		}

		public void Start (Action<T> proc, int cores)
		{
			if (proc == null)
				throw new ArgumentNullException ("proc");

			this.running = true;
			this.processor = proc;

			this.queues = new Tuple<ConcurrentQueue<T>, ManualResetEvent>[cores];

			for (int i = 0; i < cores; ++i)
			{
				var state = new Tuple<ConcurrentQueue<T>, ManualResetEvent> (new ConcurrentQueue<T>(), new ManualResetEvent (false));
				Thread processingThread = new Thread (Processor);
				processingThread.Start (state);

				this.processingThreads.Add (processingThread);
				this.queues[i] = state;
			}
		}

		public void Stop()
		{
			if (!this.running)
				return;

			this.nextQueue = 0;
			this.running = false;

			for (int i = 0; i < this.processingThreads.Count; ++i)
			{
				this.queues[i].Item2.Set();
				this.processingThreads[i].Join();
			}

			this.queues = null;
			this.processingThreads.Clear();
		}

		private volatile bool running;
		private int nextQueue = -1;
		private Tuple<ConcurrentQueue<T>, ManualResetEvent>[] queues;
		private readonly List<Thread> processingThreads = new List<Thread>();
		private Action<T> processor;

		private void Processor (object state)
		{
			Action<T> proc = this.processor;
			Tuple<ConcurrentQueue<T>, ManualResetEvent> t = (Tuple<ConcurrentQueue<T>, ManualResetEvent>) state;
			ConcurrentQueue<T> q = t.Item1;
			ManualResetEvent wait = t.Item2;

			while (this.running)
			{
				wait.WaitOne();

				T element;
				while (q.TryDequeue (out element))
					proc (element);

				wait.Reset();
			}
		}
	}
}