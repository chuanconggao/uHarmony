using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;

namespace uharmony_classify
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

    static class StreamReaderExtensionMethods
    {
        public static IEnumerable<string> ReadLines(this StreamReader reader)
        {
            while (!reader.EndOfStream)
            {
                yield return reader.ReadLine();
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
        public LabelColumn(int size, int valueNum)
            : base(size, 0, valueNum)
        {
        }
    }

    class Rule
    {
        public KeyValuePair<int, int>[] Itemset;
        public double Support;
        public double[] Confidences;
        public int CoverNumber;
    }

    class Program
    {
        static LabelColumn labels;
        static Column[] columns;
        static bool[] masks;

        static void Main(string[] args)
        {
            ReadData(args[0], Int32.Parse(args[1]));

            classify(args[2], args[3]);
        }

        private static void classify(string ruleFilename, string filename)
        {
            StringBuilder[] lines = new StringBuilder[labels.Count];

            Regex ruleBodyRegex = new Regex(@"{(?<body>[^}]*)} *: *(?<sup>[0-9]+(?:\.[0-9]+)?) *\((?<cover>[0-9]+)\) */ *(?<conf>.*)", RegexOptions.Compiled);


            StreamReader ruleReader = new StreamReader(ruleFilename, Encoding.Default);
            Rule[] rules = ruleReader.ReadLines().Select(l =>
            {
                Match m = ruleBodyRegex.Match(l);
                Rule r = new Rule()
                {
                    Itemset = m.Groups["body"].Value.Split(',').Select(s =>
                    {
                        string[] a = s.Split(':');
                        return new KeyValuePair<int, int>(Int32.Parse(a[0].Trim()), Int32.Parse(a[1].Trim()));
                    }).ToArray(), 
                    Support = Double.Parse(m.Groups["sup"].Value), 
                    CoverNumber = Int32.Parse(m.Groups["cover"].Value),
                    Confidences = m.Groups["conf"].Value.Split(',').Select(s => Double.Parse(s.Split(':')[1].Trim())).ToArray()
                };
                int n = r.Confidences.Count(d => d < 0.0);
                double c = r.Confidences.Where(d => d >= 0.0).Sum();
                for (int i = 0; i < r.Confidences.Length; i++)
                {
                    if (r.Confidences[i] < 0.0)
                    {
                        r.Confidences[i] = (1.0 - c) / n;
                    }
                }
                return r;
            }).ToArray();
            ruleReader.Close();

            StreamWriter writer = new StreamWriter(filename, false, Encoding.Default);
            int correctNum = 0;
            Double[] predicts = new Double[labels.ValueNum];
            for (int i = 0; i < lines.Length; i++)
            {
                for (int k = 0; k < predicts.Length; k++)
                {
                    predicts[k] = 0.0;
                }
                foreach (Rule r in rules)
                {
                    double p = calcProb(r, i);
                    if (p > 0.0)
                    {
                        for (int k = 0; k < predicts.Length; k++)
                        {
                            if (r.Confidences[k] >= 0.0)
                            {
                                double v = r.Confidences[k] * p;
                                //if (v > predicts[k])
                                {
                                    predicts[k] += v;
                                }
                            }
                        }
                    }
                }

                int label = -1;
                double maxv = -1.0;
                for (int k = 0; k < predicts.Length; k++)
                {
                    if (predicts[k] > maxv)
                    {
                        maxv = predicts[k];
                        label = k;
                    }
                }
                if (label == labels[i])
                {
                    correctNum++;
                }
                writer.WriteLine(label.ToString());
            }
            writer.Close();
            Console.WriteLine("Accuracy: " + String.Format("{0:0.0###}", (double)correctNum / (double)lines.Length));
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
