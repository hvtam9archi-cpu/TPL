using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TPL
{
	internal static class PdfPageRangeParser
	{
		public static List<List<int>> ParseGroups(string text, int pageCount)
		{
			ValidatePageCount(pageCount);
			if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Enter at least one page range.");

			List<string> tokens = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(token => token.Trim())
				.Where(token => token.Length > 0)
				.ToList();
			if (tokens.Count == 0) throw new InvalidDataException("Enter at least one page range.");
			return tokens.Select(token => ParseToken(token, pageCount)).ToList();
		}

		public static List<int> ParseSelection(string text, int pageCount, bool allowBlankAsAll)
		{
			ValidatePageCount(pageCount);
			if (string.IsNullOrWhiteSpace(text))
			{
				if (allowBlankAsAll) return Enumerable.Range(0, pageCount).ToList();
				throw new InvalidDataException("Enter at least one page or page range.");
			}

			var result = new List<int>();
			var seen = new HashSet<int>();
			foreach (string token in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				foreach (int pageIndex in ParseToken(token.Trim(), pageCount))
				{
					if (seen.Add(pageIndex)) result.Add(pageIndex);
				}
			}
			if (result.Count == 0) throw new InvalidDataException("Enter at least one page or page range.");
			return result;
		}

		private static List<int> ParseToken(string token, int pageCount)
		{
			if (string.IsNullOrWhiteSpace(token)) throw new InvalidDataException("A page range is empty.");
			string[] parts = token.Split('-');
			if (parts.Length == 1)
			{
				int page = ParsePageNumber(parts[0], pageCount);
				return new List<int> { page - 1 };
			}
			if (parts.Length != 2) throw new InvalidDataException($"Invalid page range: '{token}'.");

			int start = ParsePageNumber(parts[0], pageCount);
			int end = ParsePageNumber(parts[1], pageCount);
			if (start > end) throw new InvalidDataException($"The first page must not exceed the last page in '{token}'.");
			return Enumerable.Range(start - 1, end - start + 1).ToList();
		}

		private static int ParsePageNumber(string value, int pageCount)
		{
			if (!int.TryParse(value.Trim(), out int page) || page < 1 || page > pageCount)
				throw new InvalidDataException($"Page '{value.Trim()}' must be between 1 and {pageCount}.");
			return page;
		}

		private static void ValidatePageCount(int pageCount)
		{
			if (pageCount <= 0) throw new InvalidOperationException("Open a source PDF first.");
		}
	}
}
