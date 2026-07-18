using System;
using Prima.VinaCAD.ApplicationServices;

namespace TPL
{
	/// <summary>
	/// VinaCAD 2026 đôi khi ném lỗi eNotImplementedYet khi gọi Document.LockDocument().
	/// Lớp này bọc LockDocument() an toàn để bỏ qua lỗi đó (vì trong nhiều trường hợp command đã lock sẵn doc,
	/// hoặc VinaCAD không yêu cầu lock).
	/// </summary>
	public class SafeDocLock : IDisposable
	{
		private readonly DocumentLock _lock;

		public SafeDocLock(Document doc)
		{
			if (doc == null || doc.IsDisposed) return;
			try
			{
				_lock = doc.LockDocument();
			}
			catch (Exception ex)
			{
				// Ignore eNotImplementedYet or any other lock exception
				// Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[TPL Debug] SafeDocLock bypassed exception: {ex.Message}"); 
			}
		}

		public void Dispose()
		{
			if (_lock != null)
			{
				try
				{
					_lock.Dispose();
				}
				catch { }
			}
		}
	}
}
