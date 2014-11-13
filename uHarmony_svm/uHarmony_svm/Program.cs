using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace uHarmony_svm
{
    static class IEnumerableExtensionMethods
    {
        public static IEnumerable<Tdst> Merge<Tsrc1, Tsrc2, Tdst>(this IEnumerable<Tsrc1> src1, IEnumerable<Tsrc2> src2,
            Func<Tsrc1, Tsrc2, Tdst> func)
        {
            IEnumerator<Tsrc1> src1Enum = src1.GetEnumerator();
            IEnumerator<Tsrc2> src2Enum = src2.GetEnumerator();

            while (src1Enum.MoveNext() && src2Enum.MoveNext())
            {
                yield return func(src1Enum.Current, src2Enum.Current);
            }
        }
    }

    abstract class Column
    {
        public int ID;

        public int ValueNum;
    }

    abstract class Column<T> : Column
    {
        protected T[] values;

        public T this[int i]
        {
            get
            {
                return values[i];
            }
            set
            {
                values[i] = value;
            }
        }

        public int Count
        {
            get
            {
                return values.Length;
            }
        }
    }

    class CertainColumn : Column<int>
    {
        public CertainColumn(int size, int id, int valueNum)
        {
            values = new int[size];
            ID = id;
            ValueNum = valueNum;
        }
    }

    class UncertainColumn : Column<double[]>
    {
        public UncertainColumn(int size, int id, int valueNum)
        {
            values = new double[size][];
            ID = id;
            ValueNum = valueNum;

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = new double[valueNum];
            }
        }
    }

    class LabelColumn : CertainColumn
    {
        public LabelColumn(int size, int valueNum) : base(size, 0, valueNum)
        {
        }
    }

    class Rule
    {
        public KeyValuePair<int, int>[] Itemset;
    }

    class Program
    {
        static LabelColumn labels;
        static Column[] columns;
        static bool[] masks;

        static void Main(string[] args)
        {
            ReadData(args[0], Int32.Parse(args[1]));

            writeSVMFile(args[2], args[3]);
        }

        private static void writeSVMFile(string ruleFilename, string filename)
        {
            StringBuilder[] lines = new StringBuilder[labels.Count];

            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = new StringBuilder(labels.ValueNum == 2 ? (labels[i] == 1 ? "+1" : "-1") : (labels[i] + 1).ToString());
            }

            Regex ruleBodyRegex = new Regex(@"{([^}]*)}", RegexOptions.Compiled);

            int featureID = 1;
            StreamReader ruleReader = new StreamReader(ruleFilename, Encoding.Default);
            while (!ruleReader.EndOfStream)
            {
                Rule r = new Rule()
                {
                    Itemset = ruleBodyRegex.Match(ruleReader.ReadLine()).Groups[1].Value.Split(',').Select(s =>
                        {
                            string[] a = s.Split(':');
                            return new KeyValuePair<int, int>(Int32.Parse(a[0].Trim()), Int32.Parse(a[1].Trim()));
                        }).ToArray()
                };
                for (int i = 0; i < lines.Length; i++)
                {
                    double p = calcProb(r, i);
                    if (p > 0.0)
                    {
                        lines[i].Append(" ").Append(featureID.ToString() + ":" + String.Format("{0:0.0###}", p));
                    }
                }
                featureID++;
            }
            ruleReader.Close();

            StreamWriter svmWriter = new StreamWriter(filename, false, Encoding.Default);
            for (int i = 0; i < lines.Length; i++)
            {
                svmWriter.WriteLine(lines[i].ToString());
            }
            svmWriter.Close();
        }

        private static double calcProb(Rule r, int row)
        {
            double p = 1.0;
            for (int i = 0; i < r.Itemset.Length; i++)
            {
                Column col = columns[r.Itemset[i].Key];
                int value = r.Itemset[i].Value;
                if (!masks[r.Itemset[i].Key])
                {
                    if (value != (col as CertainColumn)[row])
                    {
                        return 0.0;
                    }
                }
                else
                {
                    double cp = (col as UncertainColumn)[row][value];
                    if (cp == 0.0)
                    {
                        return 0.0;
                    }
                    p *= cp;
                }
            }
            return p;
        }

        private static void ReadData(string filename, int labelCol)
        {
            int lineNum = 0;
            int colNum = 0;

            int[] valueNums = null;

            StreamReader reader = new StreamReader(filename, Encoding.Default);

            do
            {
                string l = reader.ReadLine();
                if (l.StartsWith("#DATA"))
                {
                    break;
                }

                if (l.StartsWith("#LINE_NUM"))
                {
                    lineNum = Int32.Parse(reader.ReadLine());
                }
                else if (l.StartsWith("#COL_NUM"))
                {
                    colNum = Int32.Parse(reader.ReadLine());
                }
                else if (l.StartsWith("#VALUE_COUNTS"))
                {
                    valueNums = reader.ReadLine().Split(' ').Select(s => Int32.Parse(s)).ToArray();
                }
                else if (l.StartsWith("#UNCERTAIN_MASKS"))
                {
                    masks = reader.ReadLine().Split(' ').Select(s => Int32.Parse(s) != 0).ToArray();
                }
            }
            while (!reader.EndOfStream);

            columns = new Column[colNum];
            for (int k = 0; k < colNum; k++)
            {
                if (labelCol == k)
                {
                    columns[k] = new LabelColumn(lineNum, valueNums[k]);
                }
                else
                {
                    if (masks[k])
                    {
                        columns[k] = new UncertainColumn(lineNum, k, valueNums[k]);
                    }
                    else
                    {
                        columns[k] = new CertainColumn(lineNum, k, valueNums[k]);
                    }
                }
            }

            Regex inputRegex = new Regex(@"{[0-9]+ *: *[0-9]+\.[0-9]+(?: *, *[0-9]+ *: *[0-9]+\.[0-9]+)*}|(?:-1|[0-9]+)", RegexOptions.Compiled);
            Regex uncertainRegex = new Regex(@"(?<key>[0-9]) *: *(?<value>[0-9]+\.[0-9]+)", RegexOptions.Compiled);

            for (int i = 0; i < lineNum && !reader.EndOfStream; i++)
            {
                MatchCollection matches = inputRegex.Matches(reader.ReadLine());
                for (int k = 0; k < matches.Count; k++)
                {
                    if (masks[k])
                    {
                        foreach (Match um in uncertainRegex.Matches(matches[k].Value))
                        {
                            (columns[k] as UncertainColumn)[i][Int32.Parse(um.Groups["key"].Value)] = Single.Parse(um.Groups["value"].Value);
                        }
                    }
                    else
                    {
                        (columns[k] as CertainColumn)[i] = Int32.Parse(matches[k].Value);
                    }
                }
            }

            reader.Close();

            labels = columns[labelCol] as LabelColumn;
        }
    }
}
