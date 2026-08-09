using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace TPL
{
	public enum PdfToolAction
	{
		Merge,
		Split,
		Edit,
		View,
		Rotate,
		Remove,
		Extract,
		Rearrange
	}

	public sealed class PdfToolDefinition
	{
		public PdfToolDefinition(
			PdfToolAction action,
			string title,
			string caption,
			string description,
			string glyph)
		{
			Action = action;
			Title = title;
			Caption = caption;
			Description = description;
			Glyph = glyph;
		}

		public PdfToolAction Action { get; }
		public string Title { get; }
		public string Caption { get; }
		public string Description { get; }
		public string Glyph { get; }
	}

	internal sealed class PdfToolSelectedEventArgs : EventArgs
	{
		public PdfToolSelectedEventArgs(PdfToolAction action, string title)
		{
			Action = action;
			Title = title;
		}

		public PdfToolAction Action { get; }
		public string Title { get; }
	}

	public partial class PdfToolsDashboard : UserControl
	{
		public PdfToolsDashboard()
		{
			InitializeComponent();
			Tools = CreateTools();
		}

		internal event EventHandler<PdfToolSelectedEventArgs> ToolSelected;

		public IReadOnlyList<PdfToolDefinition> Tools { get; }

		private static IReadOnlyList<PdfToolDefinition> CreateTools()
		{
			return new[]
			{
				new PdfToolDefinition(PdfToolAction.Merge, "Merge PDF", "Combine multiple files", "Select two or more PDF files and combine all pages into one document.", "\uE8B7"),
				new PdfToolDefinition(PdfToolAction.Split, "Split PDF", "Select and export pages", "Open a PDF, select the required pages, then extract them into a separate file.", "\uE8C6"),
				new PdfToolDefinition(PdfToolAction.Edit, "Edit PDF", "Organize a document", "Open a PDF and use the complete page editing workspace.", "\uE70F"),
				new PdfToolDefinition(PdfToolAction.View, "PDF Viewer", "Preview every page", "Open a PDF for page navigation and high quality preview.", "\uE890"),
				new PdfToolDefinition(PdfToolAction.Rotate, "Rotate PDF pages", "Rotate left or right", "Open a PDF and rotate one or multiple selected pages.", "\uE7AD"),
				new PdfToolDefinition(PdfToolAction.Remove, "Remove PDF pages", "Delete unwanted pages", "Open a PDF and remove selected pages with undo support.", "\uE74D"),
				new PdfToolDefinition(PdfToolAction.Extract, "Extract PDF pages", "Save selected pages", "Open a PDF, select pages, and export them to a new PDF file.", "\uE896"),
				new PdfToolDefinition(PdfToolAction.Rearrange, "Rearrange PDF pages", "Drag pages into order", "Open a PDF and reorder pages using drag and drop or the move buttons.", "\uE8FD")
			};
		}

		private void ToolCard_Click(object sender, RoutedEventArgs e)
		{
			if (sender is Button button && button.DataContext is PdfToolDefinition tool)
				ToolSelected?.Invoke(this, new PdfToolSelectedEventArgs(tool.Action, tool.Title));
		}
	}
}
