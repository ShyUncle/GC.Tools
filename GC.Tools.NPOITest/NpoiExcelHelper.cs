using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GC.Tools.NPOITest
{
    public class ExcelCellItem
    {
        public ICellStyle CellStyle { get; set; }
        public object Value { get; set; }
        public int CellSpan { get; set; } = 1;
        public int RowSpan { get; set; } = 1;
        public int CellIndex { get; set; }
    }
    public class ExcelRowItem
    {
        public List<ExcelCellItem> Cells { get; set; }
        public short Height { get; set; }
        public int RowIndex { get; set; }
    }

    public class ExcelSheetItem
    {
        public List<ExcelRowItem> Rows { get; set; }
        public int SheetIndex { get; set; }
        public List<ICellStyle> Styles { get; set; }
    }
    public class ExcelWorkBook
    {
        public List<ExcelSheetItem> Sheets { get; set; }
    }

    public class NpoiExcelHelper
    {
        static void Orig()
        {
            MemoryStream ms = new MemoryStream();
            //创建Excel
            var workbook = new HSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("固定资产折旧汇总表_201911");

            //sheet.DefaultRowHeightInPoints = 30;

            string strDataFormat = "#,##0.00";//显示千分位保留两位小数
            IDataFormat formatNum = workbook.CreateDataFormat();
            var numberFormat = formatNum.GetFormat(strDataFormat);
            ICellStyle headercellStyle = workbook.CreateCellStyle();
            headercellStyle.Alignment = HorizontalAlignment.Center;
            headercellStyle.VerticalAlignment = VerticalAlignment.Center;
            var font = workbook.CreateFont();
            font.IsBold = true;
            font.FontHeightInPoints = 18;
            headercellStyle.SetFont(font);

            ICellStyle leftStyle = workbook.CreateCellStyle();
            leftStyle.CloneStyleFrom(headercellStyle);
            leftStyle.Alignment = HorizontalAlignment.Left;
            var normalFont = workbook.CreateFont();
            normalFont.IsBold = false;
            normalFont.FontHeightInPoints = 10;
            leftStyle.SetFont(normalFont);

            ICellStyle rightStyle = workbook.CreateCellStyle();
            rightStyle.CloneStyleFrom(leftStyle);
            rightStyle.Alignment = HorizontalAlignment.Right;

            ICellStyle normalStyle = workbook.CreateCellStyle();
            normalStyle.Alignment = HorizontalAlignment.Center;
            normalStyle.VerticalAlignment = VerticalAlignment.Center;
            normalStyle.SetFont(normalFont);
            normalStyle.BorderTop = BorderStyle.Thin;
            normalStyle.BorderLeft = BorderStyle.Thin;
            normalStyle.BorderRight = BorderStyle.Thin;
            normalStyle.BorderBottom = BorderStyle.Thin;
            normalStyle.WrapText = true;

            ICellStyle normalLeftStyle = workbook.CreateCellStyle();
            normalLeftStyle.CloneStyleFrom(normalStyle);
            normalLeftStyle.Alignment = HorizontalAlignment.Left;

            ICellStyle normalRightStyle = workbook.CreateCellStyle();
            normalRightStyle.CloneStyleFrom(normalStyle);
            normalRightStyle.Alignment = HorizontalAlignment.Right;
            normalRightStyle.DataFormat = numberFormat;

            sheet.CreateRow(0).Height = 20 * 20;

            sheet.SetCellRangeAddress(1, 1, 1, 7).CreateRow(1).SetRowHeightInPoints(40).AddCell(1, "2019年11月折旧汇总表").CellStyle = headercellStyle;
            sheet.SetCellRangeAddress(2, 3, 1, 2).CreateRow(2).SetRowHeightInPoints(22).AddCell(1, "编制单位：演示账套").SetCellStyle(leftStyle);
            sheet.SetCellRangeAddress(2, 2, 3, 7).GetRow(2).AddCell(3, "第1页/共1页").SetCellStyle(rightStyle);
            sheet.SetCellRangeAddress(3, 3, 3, 7).CreateRow(3).SetRowHeightInPoints(22).AddCell(3, "单位：元").SetCellStyle(rightStyle);
            var tableHeaderRow = sheet.CreateRow(4).SetRowHeightInPoints(22);
            var tableFooterRow = sheet.CreateRow(10).SetRowHeightInPoints(22);
            List<string> columns = new List<string>() { "类别编码", "类别名称", "原值", "累计折旧", "净值", "本月计提折旧", "本年计提折旧" };
            for (int i = 1; i < 8; i++)
            {
                tableHeaderRow.AddCell(i, columns[i - 1]).SetCellStyle(normalStyle);
                if (i == 2)
                {
                    tableFooterRow.AddCell(i, "合计").SetCellStyle(normalStyle);
                }
                else if (i > 2)
                {
                    tableFooterRow.CreateCell(i).SetCellStyle(normalRightStyle).SetCellValue(i * 1023);
                }
                else
                {
                    tableFooterRow.AddCell(i, "").SetCellStyle(normalStyle);
                }
            }
            for (int i = 5; i < 10; i++)
            {
                var bodyRow = sheet.CreateRow(i).SetRowHeightInPoints(22);
                for (int j = 1; j < 8; j++)
                {
                    // var cell = bodyRow.AddCell(j, j == 2 ? "机器、机械和其他生产设备" : i.ToString().PadLeft(2, '0'));
                    var cell = bodyRow.CreateCell(j);
                    if (j == 2)
                    {
                        cell.SetCellValue("机器、机械和其他生产设备");
                    }
                    else if (j == 1)
                    {
                        cell.SetCellValue(i.ToString().PadLeft(2, '0'));
                    }
                    else
                    {
                        cell.SetCellValue(j * 1204.3);
                    }
                    if (j == 1)
                        cell.SetCellStyle(normalLeftStyle);
                    else if (j > 2)
                        cell.SetCellStyle(normalRightStyle);
                    else
                        cell.SetCellStyle(normalStyle);
                }
            }
            sheet.SetColumnWidth(0, 35 * 34);
            sheet.SetColumnWidth(1, 92 * 34);
            sheet.SetColumnWidth(2, 202 * 34);
            sheet.SetColumnWidth(3, 83 * 34);
            sheet.SetColumnWidth(4, 83 * 34);
            sheet.SetColumnWidth(5, 83 * 34);
            sheet.SetColumnWidth(6, 105 * 34);
            sheet.SetColumnWidth(7, 105 * 34);
            workbook.Write(ms);
            SaveFile(ms.ToArray(), AppDomain.CurrentDomain.BaseDirectory + "\\test.xls");
        }
        static void SaveFile(byte[] data, string fullPath)
        {
            string path = fullPath.Substring(0, fullPath.LastIndexOf("\\"));

            DirectoryInfo Drr = new DirectoryInfo(path);
            if (!Drr.Exists)
            {
                Drr.Create();
            }
            using (FileStream fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush();
            }
        }
    }

    public static class NPOIExtension
    {
        /// <summary>
        /// 合并单元格
        /// </summary>
        /// <param name="sheet">要合并单元格所在的sheet</param>
        /// <param name="rowstart">开始行的索引</param>
        /// <param name="rowend">结束行的索引</param>
        /// <param name="colstart">开始列的索引</param>
        /// <param name="colend">结束列的索引</param>
        public static ISheet SetCellRangeAddress(this ISheet sheet, int rowStart, int rowEnd, int colStart, int colEnd)
        {
            CellRangeAddress cellRangeAddress = new CellRangeAddress(rowStart, rowEnd, colStart, colEnd);
            sheet.AddMergedRegion(cellRangeAddress);
            return sheet;
        }
        public static IRow SetRowHeightInPoints(this IRow row, float heightInPoints)
        {
            row.HeightInPoints = heightInPoints;
            return row;
        }


        public static ICell SetCellStyle(this ICell cell, ICellStyle style)
        {
            cell.CellStyle = style;
            return cell;
        }

        public static ICell AddCell(this ICell cell, int index, string value)
        {
            return cell.Row.AddCell(index, value);
        }
        public static ICell AddCell(this IRow row, int index, string value)
        {
            var newcell = row.CreateCell(index);
            newcell.SetCellValue(value);
            return newcell;
        }
        public static ICell AddCell(this ICell cell, int index, DateTime value)
        {
            return cell.Row.AddCell(index, value);
        }
        public static ICell AddCell(this IRow row, int index, DateTime value)
        {
            var newcell = row.CreateCell(index);
            newcell.SetCellValue(value);
            return newcell;
        }

        public static ICell AddCell(this ICell cell, int index, double value)
        {
            return cell.Row.AddCell(index, value);
        }
        public static ICell AddCell(this IRow row, int index, double value)
        {
            var newcell = row.CreateCell(index);
            newcell.SetCellValue(value);
            return newcell;
        }

        public static IRow AddRow(this ICell cell, int rowNum)
        {
            return cell.Row.Sheet.CreateRow(rowNum);
        }
        public static ICell ESetCellType(this ICell cell, CellType cellType)
        {
            cell.SetCellType(cellType);
            return cell;
        }


    }   
}
