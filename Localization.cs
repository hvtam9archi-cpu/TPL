using System.Collections.Generic;
using System.Globalization;

namespace TPL
{
    public static class L10n
    {
        public enum Language { Vietnamese, English, ChineseSimplified, Korean, Japanese }
        private static Language _lang = Language.Vietnamese;

        public static void Init()
        {
            string cult = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            switch (cult)
            {
                case "vi": _lang = Language.Vietnamese; break;
                case "zh": _lang = Language.ChineseSimplified; break;
                case "ko": _lang = Language.Korean; break;
                case "ja": _lang = Language.Japanese; break;
                default: _lang = Language.English; break;
            }
        }
        public static void Set(Language lang) => _lang = lang;
        public static Language Current => _lang;

        public static string T(string key)
        {
            Dictionary<string, string> d;
            switch (_lang)
            {
                case Language.ChineseSimplified: d = _zh; break;
                case Language.Korean: d = _ko; break;
                case Language.Japanese: d = _ja; break;
                case Language.English: d = _en; break;
                default: d = _vi; break;
            }
            return d.TryGetValue(key, out string v) ? v : (_vi.TryGetValue(key, out string vv) ? vv : key);
        }

        // ─── Vietnamese ───────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> _vi = new Dictionary<string, string>
        {
            ["app_title"] = "Xuất PDF Hàng Loạt - TPL",
            ["header_printer"] = "1. MÁY IN - GIẤY - BÚT IN",
            ["header_frame"] = "2. VÙNG IN",
            ["header_save"] = "3. LƯU TẬP TIN",
            ["header_scope"] = "4. PHẠM VI IN",
            ["header_sort"] = "5. SẮP XẾP",
            ["label_printer"] = "Máy in:",
            ["label_paper"] = "Khổ giấy:",
            ["label_style"] = "Bút in:",
            ["label_block"] = "Block:",
            ["label_layer"] = "Layer:",
            ["label_basename"] = "Tên File:",
            ["label_folder"] = "Thư mục:",
            ["label_ord1"] = "Ưu tiên 1:",
            ["label_ord2"] = "Ưu tiên 2:",
            ["label_anchor"] = "Điểm neo:",
            ["label_fuzz"] = "Sai số:",
            ["chk_merge"] = "GỘP PDF",
            ["chk_open"] = "Tự động mở file",
            ["chk_mark"] = "Đánh dấu vùng in",
            ["rb_all"] = "Tất cả Layout",
            ["rb_current"] = "Layout hiện hành",
            ["rb_manual"] = "Chọn thủ công",
            ["btn_plot"] = "BẮT ĐẦU IN",
            ["btn_cancel"] = "Hủy (Thoát)",
            ["btn_browse"] = "Browse",
            ["btn_select"] = "Select",
            ["sort_lr"] = "Trái → Phải",
            ["sort_rl"] = "Phải → Trái",
            ["sort_tb"] = "Trên → Dưới",
            ["sort_bt"] = "Dưới → Trên",
            ["sort_sel"] = "Theo thứ tự chọn",
            ["sort_mark"] = "Theo đánh dấu",
            ["sort_none"] = "None",
            ["anchor_bl"] = "Dưới-Trái",
            ["anchor_br"] = "Dưới-Phải",
            ["anchor_tl"] = "Trên-Trái",
            ["anchor_tr"] = "Trên-Phải",
            ["msg_no_frame"] = "Vui lòng chọn mẫu Block hoặc Layer hợp lệ.",
            ["msg_no_manual"] = "Chưa chọn bản vẽ nào.\nVui lòng bấm 'Select' để chọn.",
            ["msg_no_result"] = "Không tìm thấy khung in nào hợp lệ.",
            ["msg_plot_error"] = "Lỗi trong quá trình in: {0}",
            ["msg_sel_block"] = "\nChọn các Block khung tên mẫu: ",
            ["msg_sel_layer"] = "\nChọn các Polyline khung tên mẫu: ",
            ["msg_sel_frames"] = "\nChọn các khung in: ",
            ["msg_need_sample"] = "Vui lòng chọn Block hoặc Layer mẫu trước!",
            ["err_title"] = "Lỗi",
            ["warn_title"] = "Chú ý",
            ["prog_title"] = "Đang xuất PDF...",
            ["prog_progress"] = "Tiến trình: {0} / {1}",
            ["prog_file"] = "Tập tin: {0}",
            ["prog_merging"] = "Đang gộp PDF...",
        };

        // ─── English ──────────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> _en = new Dictionary<string, string>
        {
            ["app_title"] = "Batch Plot PDF - TPL",
            ["header_printer"] = "1. PRINTER - PAPER SIZE - PLOT STYLE",
            ["header_frame"] = "2. PLOT FRAMES",
            ["header_save"] = "3. OUTPUT",
            ["header_scope"] = "4. SCOPE",
            ["header_sort"] = "5. SORT ORDER",
            ["label_printer"] = "Printer:",
            ["label_paper"] = "Paper size:",
            ["label_style"] = "Plot style:",
            ["label_block"] = "Block:",
            ["label_layer"] = "Layer:",
            ["label_basename"] = "File Name:",
            ["label_folder"] = "Folder:",
            ["label_ord1"] = "Priority 1:",
            ["label_ord2"] = "Priority 2:",
            ["label_anchor"] = "Base Point:",
            ["label_fuzz"] = "Tolerance:",
            ["chk_merge"] = "Merge PDF",
            ["chk_open"] = "Open when done",
            ["chk_mark"] = "Mark plot regions",
            ["rb_all"] = "All Layouts",
            ["rb_current"] = "Current Layout",
            ["rb_manual"] = "Manual Select",
            ["btn_plot"] = "START PLOT",
            ["btn_cancel"] = "Cancel (Exit)",
            ["btn_browse"] = "Browse",
            ["btn_select"] = "Select",
            ["sort_lr"] = "Left → Right",
            ["sort_rl"] = "Right → Left",
            ["sort_tb"] = "Top → Bottom",
            ["sort_bt"] = "Bottom → Top",
            ["sort_sel"] = "Selection order",
            ["sort_mark"] = "By markers",
            ["sort_none"] = "None",
            ["anchor_bl"] = "Bottom-Left",
            ["anchor_br"] = "Bottom-Right",
            ["anchor_tl"] = "Top-Left",
            ["anchor_tr"] = "Top-Right",
            ["msg_no_frame"] = "Please select a valid Block or Layer.",
            ["msg_no_manual"] = "No frames selected.\nPlease click 'Select' first.",
            ["msg_no_result"] = "No valid plot frames found.",
            ["msg_plot_error"] = "Error during plotting: {0}",
            ["msg_sel_block"] = "\nSelect block frame templates: ",
            ["msg_sel_layer"] = "\nSelect polyline frame templates: ",
            ["msg_sel_frames"] = "\nSelect frames to plot: ",
            ["msg_need_sample"] = "Please select a Block or Layer template first!",
            ["err_title"] = "Error",
            ["warn_title"] = "Warning",
            ["prog_title"] = "Exporting PDF...",
            ["prog_progress"] = "Progress: {0} / {1}",
            ["prog_file"] = "File: {0}",
            ["prog_merging"] = "Merging PDF...",
            ["btn_delete_marks"] = "Delete",
            ["lbl_ready"] = "Ready",
        };

        // ─── Chinese (Simplified) 简体中文 ─────────────────────────────────────
        private static readonly Dictionary<string, string> _zh = new Dictionary<string, string>
        {
            ["app_title"] = "批量打印PDF - TPL",
            ["header_printer"] = "1. 打印机 & 纸张",
            ["header_frame"] = "2. 打印框架",
            ["header_save"] = "3. 输出设置",
            ["header_scope"] = "4. 打印范围",
            ["header_sort"] = "5. 排序方式",
            ["label_printer"] = "打印机:",
            ["label_paper"] = "纸张:",
            ["label_style"] = "样式:",
            ["label_block"] = "图块:",
            ["label_layer"] = "图层:",
            ["label_basename"] = "基础名称:",
            ["label_folder"] = "输出文件夹:",
            ["label_ord1"] = "优先级 1:",
            ["label_ord2"] = "优先级 2:",
            ["label_anchor"] = "基点:",
            ["label_fuzz"] = "容差:",
            ["chk_merge"] = "合并 PDF",
            ["chk_open"] = "完成后打开文件",
            ["chk_mark"] = "标记打印区域",
            ["rb_all"] = "所有布局",
            ["rb_current"] = "当前布局",
            ["rb_manual"] = "手动选择",
            ["btn_plot"] = "开始打印",
            ["btn_cancel"] = "取消（退出）",
            ["btn_browse"] = "浏览",
            ["btn_select"] = "选择",
            ["sort_lr"] = "从左到右",
            ["sort_rl"] = "从右到左",
            ["sort_tb"] = "从上到下",
            ["sort_bt"] = "从下到上",
            ["sort_sel"] = "按选择顺序",
            ["sort_mark"] = "按标记顺序",
            ["sort_none"] = "无",
            ["anchor_bl"] = "左下",
            ["anchor_br"] = "右下",
            ["anchor_tl"] = "左上",
            ["anchor_tr"] = "右上",
            ["msg_no_frame"] = "请选择有效的图块或图层。",
            ["msg_no_manual"] = "未选择任何图框。\n请点击\u300C选择\u300D按钮。",
            ["msg_no_result"] = "未找到有效的打印框架。",
            ["msg_plot_error"] = "打印过程中出错: {0}",
            ["msg_sel_block"] = "\n选择样板图块框架: ",
            ["msg_sel_layer"] = "\n选择样板多段线框架: ",
            ["msg_sel_frames"] = "\n选择要打印的框架: ",
            ["msg_need_sample"] = "请先选择图块或图层样板！",
            ["err_title"] = "错误",
            ["warn_title"] = "警告",
            ["prog_title"] = "正在导出PDF...",
            ["prog_progress"] = "进度: {0} / {1}",
            ["prog_file"] = "文件: {0}",
            ["prog_merging"] = "正在合并PDF...",
        };

        // ─── Korean 한국어 ─────────────────────────────────────────────────────
        private static readonly Dictionary<string, string> _ko = new Dictionary<string, string>
        {
            ["app_title"] = "일괄 PDF 출력 - TPL",
            ["header_printer"] = "1. 프린터 & 용지",
            ["header_frame"] = "2. 출력 영역",
            ["header_save"] = "3. 출력 설정",
            ["header_scope"] = "4. 출력 범위",
            ["header_sort"] = "5. 정렬 순서",
            ["label_printer"] = "프린터:",
            ["label_paper"] = "용지:",
            ["label_style"] = "스타일:",
            ["label_block"] = "블록:",
            ["label_layer"] = "레이어:",
            ["label_basename"] = "기본 이름:",
            ["label_folder"] = "저장 폴더:",
            ["label_ord1"] = "우선순위 1:",
            ["label_ord2"] = "우선순위 2:",
            ["label_anchor"] = "기준점:",
            ["label_fuzz"] = "허용 오차:",
            ["chk_merge"] = "PDF 병합",
            ["chk_open"] = "완료 시 파일 열기",
            ["chk_mark"] = "출력 영역 표시",
            ["rb_all"] = "모든 레이아웃",
            ["rb_current"] = "현재 레이아웃",
            ["rb_manual"] = "수동 선택",
            ["btn_plot"] = "출력 시작",
            ["btn_cancel"] = "취소 (종료)",
            ["btn_browse"] = "찾아보기",
            ["btn_select"] = "선택",
            ["sort_lr"] = "왼쪽 → 오른쪽",
            ["sort_rl"] = "오른쪽 → 왼쪽",
            ["sort_tb"] = "위 → 아래",
            ["sort_bt"] = "아래 → 위",
            ["sort_sel"] = "선택 순서",
            ["sort_mark"] = "표시 순서",
            ["sort_none"] = "없음",
            ["anchor_bl"] = "왼쪽 하단",
            ["anchor_br"] = "오른쪽 하단",
            ["anchor_tl"] = "왼쪽 상단",
            ["anchor_tr"] = "오른쪽 상단",
            ["msg_no_frame"] = "유효한 블록 또는 레이어를 선택하세요.",
            ["msg_no_manual"] = "도면 프레임이 선택되지 않았습니다.\n'선택' 버튼을 클릭하세요.",
            ["msg_no_result"] = "유효한 출력 프레임을 찾을 수 없습니다.",
            ["msg_plot_error"] = "출력 중 오류: {0}",
            ["msg_sel_block"] = "\n샘플 블록 프레임 선택: ",
            ["msg_sel_layer"] = "\n샘플 폴리라인 프레임 선택: ",
            ["msg_sel_frames"] = "\n출력할 프레임 선택: ",
            ["msg_need_sample"] = "먼저 블록 또는 레이어 샘플을 선택하세요!",
            ["err_title"] = "오류",
            ["warn_title"] = "경고",
            ["prog_title"] = "PDF 내보내는 중...",
            ["prog_progress"] = "진행: {0} / {1}",
            ["prog_file"] = "파일: {0}",
            ["prog_merging"] = "PDF 병합 중...",
        };

        // ─── Japanese 日本語 ───────────────────────────────────────────────────
        private static readonly Dictionary<string, string> _ja = new Dictionary<string, string>
        {
            ["app_title"] = "一括PDF出力 - TPL",
            ["header_printer"] = "1. プリンター & 用紙",
            ["header_frame"] = "2. 印刷フレーム",
            ["header_save"] = "3. 出力設定",
            ["header_scope"] = "4. 印刷範囲",
            ["header_sort"] = "5. 並び順",
            ["label_printer"] = "プリンター:",
            ["label_paper"] = "用紙:",
            ["label_style"] = "スタイル:",
            ["label_block"] = "ブロック:",
            ["label_layer"] = "レイヤー:",
            ["label_basename"] = "基本名:",
            ["label_folder"] = "保存先:",
            ["label_ord1"] = "優先度 1:",
            ["label_ord2"] = "優先度 2:",
            ["label_anchor"] = "基点:",
            ["label_fuzz"] = "許容差:",
            ["chk_merge"] = "PDF を結合",
            ["chk_open"] = "完了後にファイルを開く",
            ["chk_mark"] = "印刷領域をマーク",
            ["rb_all"] = "すべてのレイアウト",
            ["rb_current"] = "現在のレイアウト",
            ["rb_manual"] = "手動選択",
            ["btn_plot"] = "印刷開始",
            ["btn_cancel"] = "キャンセル（終了）",
            ["btn_browse"] = "参照",
            ["btn_select"] = "選択",
            ["sort_lr"] = "左 → 右",
            ["sort_rl"] = "右 → 左",
            ["sort_tb"] = "上 → 下",
            ["sort_bt"] = "下 → 上",
            ["sort_sel"] = "選択順",
            ["sort_mark"] = "マーク順",
            ["sort_none"] = "なし",
            ["anchor_bl"] = "左下",
            ["anchor_br"] = "右下",
            ["anchor_tl"] = "左上",
            ["anchor_tr"] = "右上",
            ["msg_no_frame"] = "有効なブロックまたはレイヤーを選択してください。",
            ["msg_no_manual"] = "フレームが選択されていません。\n「選択」ボタンをクリックしてください。",
            ["msg_no_result"] = "有効な印刷フレームが見つかりませんでした。",
            ["msg_plot_error"] = "印刷中にエラーが発生: {0}",
            ["msg_sel_block"] = "\nサンプルブロックフレームを選択: ",
            ["msg_sel_layer"] = "\nサンプルポリラインフレームを選択: ",
            ["msg_sel_frames"] = "\n印刷するフレームを選択: ",
            ["msg_need_sample"] = "先にブロックまたはレイヤーのサンプルを選択してください！",
            ["err_title"] = "エラー",
            ["warn_title"] = "警告",
            ["prog_title"] = "PDF を書き出し中...",
            ["prog_progress"] = "進捗: {0} / {1}",
            ["prog_file"] = "ファイル: {0}",
            ["prog_merging"] = "PDF を結合中...",
        };
    }
}

