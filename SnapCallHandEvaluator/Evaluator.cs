using System;
using System.IO;

namespace SnapCall
{
	// taken from https://github.com/platatat/SnapCall
	public class Evaluator
	{
		private bool debug;
		private HashMap handRankMap;

		public Evaluator(string fileName)
		{
			bool debug = false;
			DateTime start = DateTime.UtcNow;
			this.debug = debug;

			// Load hand rank table
			LoadFromFile(fileName);
			TimeSpan elapsed = DateTime.UtcNow - start;
			if (debug) Console.WriteLine("Hand evaluator setup completed in {0:0.00}s", elapsed.TotalSeconds);
		}

		public int Evaluate(ulong bitmap)
		{
			// bitmap is expected to represent a 5-card hand
			return (int)handRankMap[bitmap];
		}

		private void LoadFromFile(string path)
		{
			using (FileStream inputStream = new FileStream(path, FileMode.Open))
			using (MemoryStream memoryStream = new MemoryStream())
			{
				inputStream.CopyTo(memoryStream);
				handRankMap = HashMap.Deserialize(memoryStream.ToArray());
			}
		}
	}
}
