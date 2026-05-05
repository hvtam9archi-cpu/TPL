using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using System.Collections.Generic;
using System.Linq;

namespace TPL
{
    public class PlotHelper
    {
        public enum FrameType
        {
            Block,
            Polyline
        }

        public enum SelectionMode
        {
            AllLayouts,
            CurrentLayout,
            Manual
        }

        public enum SortOrder
        {
            LeftToRight,
            RightToLeft,
            TopToBottom,
            BottomToTop,
            SelectionOrder,
            MarkedOrder,
            None
        }

        public enum BasePoint
        {
            BottomLeft,
            BottomRight,
            TopLeft,
            TopRight
        }

        public class PlotSettingsData
        {
            public string DeviceName { get; set; }
            public string PaperSize { get; set; }
            public string PlotStyle { get; set; }
            public string OutputPath { get; set; }
            public string BaseFileName { get; set; }
            public FrameType FrameType { get; set; }
            public List<string> FrameNames { get; set; } = new List<string>();
            public SelectionMode SelectionMode { get; set; }
            public SortOrder GroupOrder { get; set; } // ORD1
            public SortOrder CrossGroupOrder { get; set; } // ORD2
            public BasePoint SortBasePoint { get; set; }
            public double Fuzz { get; set; }
            public bool MarkPlotRegions { get; set; }
            public bool MergePdfs { get; set; }
            public bool OpenPdf { get; set; }
            public bool ConvertToImage { get; set; }
            public bool PdfEditor { get; set; }
            public string ImageFormat { get; set; } = "PNG";
            public int ImageDpi { get; set; } = 300;
            public List<ObjectId> ManualSelectionIds { get; set; } = new List<ObjectId>();
        }

        public static List<string> GetPrinters()
        {
            PlotSettingsValidator psv = PlotSettingsValidator.Current;
            return psv.GetPlotDeviceList().Cast<string>().ToList();
        }

        public static List<string> GetPaperSizes(string deviceName)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            List<string> papers = new();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                using (PlotSettings ps = new(doc.Database.TileMode))
                {
                    try
                    {
                        PlotSettingsValidator psv = PlotSettingsValidator.Current;
                        psv.SetPlotConfigurationName(ps, deviceName, null);
                        psv.RefreshLists(ps);
                        papers = psv.GetCanonicalMediaNameList(ps).Cast<string>().Select(p => p.Replace("_", " ")).ToList();
                    }
                    catch { }
                }
                tr.Commit();
            }
            return papers;
        }

        public static List<string> GetPlotStyles()
        {
            PlotSettingsValidator psv = PlotSettingsValidator.Current;
            return psv.GetPlotStyleSheetList().Cast<string>().ToList();
        }

        public static List<string> GetBlockNames()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            List<string> blocks = new();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId btrId in bt)
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    if (!btr.IsAnonymous && !btr.IsLayout)
                    {
                        blocks.Add(btr.Name);
                    }
                }
                tr.Commit();
            }
            blocks.Sort();
            return blocks;
        }

        public static List<string> GetLayerNames()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            List<string> layers = new();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId ltrId in lt)
                {
                    LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(ltrId, OpenMode.ForRead);
                    layers.Add(ltr.Name);
                }
                tr.Commit();
            }
            layers.Sort();
            return layers;
        }
    }
}
