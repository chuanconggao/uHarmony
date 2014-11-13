//#define APPROXIMATE_EXP
#define PRUNING

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace uHarmony
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

    class ColumnComparer : IComparer<Column>
    {
        int getValue(Column x)
        {
            if (x is LabelColumn)
            {
                return 0;
            }
            else if (x is CertainColumn)
            {
                return 1;
            }
            else if (x is UncertainColumn)
            {
                return 2;
            }
            else
            {
                return -1;
            }
        }

        public int Compare(Column x, Column y)
        {
            return getValue(x).CompareTo(getValue(y));
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
        static CertainColumn[] certainColumns;
        static UncertainColumn[] uncertainColumns;

        static Dictionary<int, Rule> rules = new Dictionary<int,Rule>();
        static LinkedList<KeyValuePair<int, double>>[] covers;

        static double minSup;
        static double minCoverProb;

        static void Main(string[] args)
        {
            ReadData(args[0], Int32.Parse(args[1]));
            minSup = Double.Parse(args[2]) * labels.Count;
            minCoverProb = Single.Parse(args[3]);

            mine();

            StreamWriter ruleWriter = new StreamWriter(args[4], false, Encoding.Default);
            foreach (var r in rules.OrderByDescending(p => p.Value.CoverNumber))
            {
                ruleWriter.WriteLine("{" + r.Value.Itemset.OrderBy(p => columns[p.Key].ID).Select(p => columns[p.Key].ID.ToString() + ": " + p.Value.ToString())
                    .Aggregate((s, c) => s + ", " + c) + "} : " + String.Format("{0:0.####}", r.Value.Support) + " (" + r.Value.CoverNumber + ")" +
                    " / " + r.Value.Confidences.Select((d, i) => i.ToString() + ": " + String.Format("{0:0.0###}", d)).Aggregate((s, c) => s + ", " + c));
            }
            ruleWriter.Close();

            //Console.ReadKey();
        }

        private static void ReadData(string filename, int labelCol)
        {
            int lineNum = 0;
            int colNum = 0;

            int[] valueNums = null;

            bool[] masks = null;

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
            columns = columns.Where((c, k) => valueNums[k] > 1).SkipWhile((c, k) => k == labelCol)
                .OrderBy(c => c, new ColumnComparer()).ThenBy(c => c.ID).ToArray();
            certainColumns = columns.TakeWhile(c => c is CertainColumn).Select(c => c as CertainColumn).ToArray();
            uncertainColumns = columns.Skip(certainColumns.Length).Select(c => c as UncertainColumn).ToArray();

            covers = new LinkedList<KeyValuePair<int, double>>[labels.Count];
            for (int i = 0; i < labels.Count; i++)
            {
                covers[i] = new LinkedList<KeyValuePair<int, double>>();
            }
        }

        private static void mine()
        {
            int sup = labels.Count;
            double[] nextPosSups = new double[labels.ValueNum];
            for (int k = 0; k < labels.ValueNum; k++)
            {
                nextPosSups[k] = 0;
            }
            int[] indexes = new int[sup];
            for (int i = 0; i < sup; i++)
            {
                nextPosSups[labels[i]]++;
                indexes[i] = i;
            }
            Stack<KeyValuePair<int, int>> itemStack = new Stack<KeyValuePair<int, int>>();

            double[] maxConfs = new double[labels.ValueNum];
            for (int i = 0; i < labels.ValueNum; i++)
            {
                maxConfs[i] = nextPosSups[i] / (double) sup;
            }
            mine_rec(-1, indexes, sup, sup, itemStack, null, nextPosSups, maxConfs);
        }

        static int ruleID = 0;

        private static void swap(ref double[] a, ref double[] b)
        {
            double[] t = a;
            a = b;
            b = t;
        }

        private static double calcConfExp(int[] curLabels, int curNum, double[] probs, double maxConf, int posLabel, double posSup)
        {
            int n = curNum;
            double[] e1Old = new double[n + 1];
            double[] e1New = new double[n + 1];
            double[] exOld = new double[n + 1];
            double[] exNew = new double[n + 1];

            for (int i = 0; i < exOld.Length; i++)
            {
                exOld[i] = 0;
            }
            e1Old[0] = 1.0;
            for (int i = 1; i < e1Old.Length; i++)
            {
                e1Old[i] = e1Old[i - 1] * (1.0 - probs[i - 1]);
            }

            double exp = 0.0;

#if PRUNING
            double s = 0.0;
            double bound = 0.0;
#endif
            for (int i = 1; i <= n; i++)
            {
#if PRUNING
                bound = i == 1 ? posSup : bound - (posSup - s) / (double) (i * (i - 1));
                if (i < n && bound <= maxConf)
                {
                    return -1.0;
                }
#endif

                int j = i;
                double p = probs[j - 1];
                e1New[j] = e1Old[j - 1] * p;
                exNew[j] = exOld[j - 1] * p;
                if (curLabels[j - 1] == posLabel)
                {
                    exNew[j] += e1Old[j - 1] * p;
                }
                for (j = i + 1; j <= n; j++)
                {
                    p = probs[j - 1];
                    e1New[j] = e1Old[j - 1] * p + e1New[j - 1] * (1.0 - p);
                    exNew[j] = exOld[j - 1] * p + exNew[j - 1] * (1.0 - p);
                    if (curLabels[j - 1] == posLabel)
                    {
                        exNew[j] += e1Old[j - 1] * p;
                    }
                }
                exp += exNew[n] / i;

#if PRUNING
                s += exNew[n];
#endif

                swap(ref e1New, ref e1Old);
                swap(ref exNew, ref exOld);
            }

            return exp;
        }

		/*
        private static double calcConfExp_temp(int[] curLabels, int curNum, double[] probs, double maxConf, int posLabel, double posSup)
        {
            int n = curNum;
            double[] e1Old = new double[n + 1];
            double[] e1New = new double[n + 1];
            double[] exOld = new double[n + 1];
            double[] exNew = new double[n + 1];

            for (int i = 0; i < exOld.Length; i++)
            {
                exOld[i] = 0;
            }
            e1Old[0] = 1.0;
            for (int i = 1; i < e1Old.Length; i++)
            {
                e1Old[i] = e1Old[i - 1] * (1.0 - probs[i - 1]);
            }

            double exp = 0.0;

#if PRUNING
            double s = 0.0;
            double bound = 0.0;
#endif
            for (int i = 1; i <= n; i++)
            {
#if PRUNING
                bound = i == 1 ? posSup : bound - (posSup - s) / (double) (i * (i - 1));
                Console.WriteLine(i.ToString() + " : " + bound);
                if (i < n && bound <= maxConf)
                {
                    //return -1.0;
                }
#endif

                int j = i;
                double p = probs[j - 1];
                e1New[j] = e1Old[j - 1] * p;
                exNew[j] = exOld[j - 1] * p;
                if (curLabels[j - 1] == posLabel)
                {
                    exNew[j] += e1Old[j - 1] * p;
                }
                for (j = i + 1; j <= n; j++)
                {
                    p = probs[j - 1];
                    e1New[j] = e1Old[j - 1] * p + e1New[j - 1] * (1.0 - p);
                    exNew[j] = exOld[j - 1] * p + exNew[j - 1] * (1.0 - p);
                    if (curLabels[j - 1] == posLabel)
                    {
                        exNew[j] += e1Old[j - 1] * p;
                    }
                }
                exp += exNew[n] / i;

#if PRUNING
                s += exNew[n];
#endif

                swap(ref e1New, ref e1Old);
                swap(ref exNew, ref exOld);
            }

            return exp;
        }
		*/

        private static void mine_rec(int curColIndex, int[] indexes, int curNum, double curSup, 
            Stack<KeyValuePair<int, int>> stack, double[] probs, double[] curPosSups, double[] prevMaxConfs)
        {
            double[] maxConfs = prevMaxConfs.ToArray();

            double[] curConfs = new double[maxConfs.Length];

            if (curColIndex < certainColumns.Length)
            {
                for (int i = 0; i < curConfs.Length; i++)
                {
                    curConfs[i] = curPosSups[i] / curSup;
                }
            }
            else
            {
#if APPROXIMATE_EXP
                for (int i = 0; i < curConfs.Length; i++)
                {
                    curConfs[i] = curPosSups[i] / curSup;
                }
#else
                int[] curLabels = indexes.Select(k => labels[k]).ToArray();
                double s = 0.0;
                int f = -1;
                for (int i = curConfs.Length - 1; i >= 0; i--)
                {
                    if (curPosSups[i] < curSup && curPosSups[i] > 0.0)
                    {
                        f = i;
                        break;
                    }

                }
                for (int i = 0; i < curConfs.Length; i++)
                {
                    if (curPosSups[i] == 0.0)
                    {
                        curConfs[i] = 0.0;
                    }
                    else if (curPosSups[i] == curSup)
                    {
                        curConfs[i] = 1.0;
                    }
                    else
                    {
                        if (i == f)
                        {
                            curConfs[i] = 1.0 - s;
                        }
                        else
                        {
                            curConfs[i] = calcConfExp(curLabels, curNum, probs, maxConfs[i], i, curPosSups[i]);
                            if (curConfs[i] != -1.0)
                            {
                                s += curConfs[i];
                            }
                            else
                            {
								/*
                                calcConfExp_temp(curLabels, curNum, probs, maxConfs[i], i, curPosSups[i]);
                                Console.WriteLine("{" + stack.ToArray().OrderBy(p => columns[p.Key].ID).Select(p => columns[p.Key].ID.ToString() + ": " + p.Value.ToString())
                                    .Aggregate((q, c) => q + ", " + c) + "}");
								*/
                                f = -1;
                            }
                        }
                    }
                }
#endif
            }

            Rule rule = new Rule()
            {
                Itemset = stack.ToArray(),
                Support = curSup,
                Confidences = curConfs, 
                CoverNumber = 0
            };
            for (int i = 0; i < maxConfs.Length; i++)
            {
                if (curConfs[i] > maxConfs[i])
                {
                    updateRules(indexes, probs, i, rule);
                    maxConfs[i] = curConfs[i];
                }
            }
            if (rule.CoverNumber > 0)
            {
                rules.Add(ruleID++, rule);
            }

            for (int nextColIndex = curColIndex + 1; nextColIndex < certainColumns.Length; nextColIndex++)
            {
                CertainColumn nextCol = columns[nextColIndex] as CertainColumn;

                int valueNum = nextCol.ValueNum;
                int[] nextSups = new int[valueNum];
                for (int i = 0; i < valueNum; i++)
                {
                    nextSups[i] = 0;
                }

                for (int i = 0; i < curNum; i++)
                {
                    int v = nextCol[indexes[i]];
                    if (v != -1)
                    {
                        nextSups[v]++;
                    }
                }

                double[] nextPosSups = new double[labels.ValueNum];
                for (int i = 0; i < valueNum; i++)
                {
                    if (nextSups[i] >= minSup)
                    {
                        for (int k = 0; k < labels.ValueNum; k++)
                        {
                            nextPosSups[k] = 0;
                        }
                        int[] nextIndexes = new int[nextSups[i]];
                        for (int j = 0, k = 0; j < nextSups[i]; k++)
                        {
                            if (nextCol[indexes[k]] == i)
                            {
                                nextPosSups[labels[indexes[k]]]++;
                                nextIndexes[j++] = indexes[k];
                            }
                        }

                        stack.Push(new KeyValuePair<int, int>(nextColIndex, i));
                        mine_rec(nextColIndex, nextIndexes, nextSups[i], nextSups[i], stack, null, nextPosSups, maxConfs);
                        stack.Pop();
                    }
                }
            }

            for (int nextColIndex = Math.Max(certainColumns.Length, curColIndex + 1); nextColIndex < columns.Length; nextColIndex++)
            {
                UncertainColumn nextCol = columns[nextColIndex] as UncertainColumn;

                int valueNum = nextCol.ValueNum;
                double[] nextSups = new double[valueNum];
                for (int i = 0; i < valueNum; i++)
                {
                    nextSups[i] = 0.0;
                }

                if (probs == null)
                {
                    probs = new double[curNum];
                    for (int i = 0; i < curNum; i++)
                    {
                        probs[i] = 1.0;
                    }
                }

                for (int i = 0; i < curNum; i++)
                {
                    for (int j = 0; j < valueNum; j++)
                    {
                        nextSups[j] += probs[i] * nextCol[indexes[i]][j];
                    }
                }

                double[] nextPosSups = new double[labels.ValueNum];
                for (int i = 0; i < valueNum; i++)
                {
                    if (nextSups[i] >= minSup)
                    {
                        for (int k = 0; k < labels.ValueNum; k++)
                        {
                            nextPosSups[k] = 0;
                        }
                        double[] nextProbs = new double[curNum];
                        for (int j = 0; j < curNum; j++)
                        {
                            double v = probs[j] * nextCol[indexes[j]][i];
                            nextProbs[j] = v;
                            nextPosSups[labels[indexes[j]]] += v;
                        }

                        stack.Push(new KeyValuePair<int, int>(nextColIndex, i));
                        mine_rec(nextColIndex, indexes, curNum, nextSups[i], stack, nextProbs, nextPosSups, maxConfs);
                        stack.Pop();
                    }
                }
            }
        }

        private static void updateRules(int[] indexes, double[] probs, int label, Rule rule)
        {
            int coverNum = 0;

            for (int i = 0; i < indexes.Length;)
            {
                if (labels[indexes[i]] != label)
                {
                    goto NEXT;
                }

                LinkedList<KeyValuePair<int, double>> coverList = covers[indexes[i]];

                var node = coverList.Count > 0 ? coverList.First : null;

                double v = 1.0;

                while (node != null)
                {
                    Rule nodeRule = rules[node.Value.Key];
                    double nodeConf = nodeRule.Confidences[label];
                    double nodeSup = nodeRule.Support;

                    if (nodeConf < rule.Confidences[label] || nodeConf == rule.Confidences[label] &&
                        (nodeSup < rule.Support || nodeSup == rule.Support &&
                        (nodeRule.Itemset.Length > rule.Itemset.Length)))
                    {
                        break;
                    }
                    else
                    {
                        if (IsSubsetOf(rule.Itemset, nodeRule.Itemset))
                        {
                            goto NEXT;
                        }

                        v *= 1.0 - node.Value.Value;
                        node = node.Next;
                    }
                }

                if (1.0 - v > minCoverProb)
                {
                    goto NEXT;
                }

                if (node == null)
                {
                    coverList.AddLast(new KeyValuePair<int, double>(ruleID, probs == null ? 1.0 : probs[i]));
                }
                else
                {
                    coverList.AddBefore(node, new KeyValuePair<int, double>(ruleID, probs == null ? 1.0 : probs[i]));
                    v *= 1.0 - (probs == null ? 1.0 : probs[i]);

                    do
                    {
                        if (1.0 - v > minCoverProb)
                        {
                            do
                            {
                                LinkedListNode<KeyValuePair<int, double>> tNode = node.Next;
                                Rule lRule = rules[node.Value.Key];
                                lRule.CoverNumber--;
                                if (lRule.CoverNumber == 0)
                                {
                                    rules.Remove(node.Value.Key);
                                }
                                coverList.Remove(node);
                                node = tNode;
                            }
                            while (node != null);
                            break;
                        }
                        v *= 1.0 - node.Value.Value;
                        node = node.Next;
                    }
                    while (node != null);
                }
                coverNum++;
            NEXT:
                i++;
            }

            if (coverNum > 0)
            {
                rule.CoverNumber += coverNum;
            }
        }

        private static bool IsSubsetOf(KeyValuePair<int, int>[] v1, KeyValuePair<int, int>[] v2)
        {
            int i = 0;
            for (int j = 0; v1[i].Key >= v2[i].Key && v1.Length - i <= v2.Length - j; j++)
            {
                if (v1[i].Key == v2[i].Key && v1[i].Value == v2[i].Value)
                {
                    i++;
                    if (i == v1.Length)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
