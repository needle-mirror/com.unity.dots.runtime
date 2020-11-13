using System;
using System.Collections.Generic;

public static class Utility
{
    static bool IsQuotes(char c)
    {
        return c == '"' || c == '\'';
    }

    static int FindSubstringBeginIndex(string input, int index, Func<char, bool> splitPredicate)
    {
        for (int i = index; i < input.Length; ++i)
        {
            if (!splitPredicate(input[i]))
                return i;
        }

        return input.Length;
    }

    static int FindSubstringEndIndex(string input, int index, Func<char, bool> splitPredicate)
    {
        if (index >= input.Length)
            return index;

        const char nullChar = '\0';
        char currentQuoteChar = nullChar;

        int resultIndex = index;

        while (resultIndex < input.Length)
        {
            char c = input[resultIndex];

            if (currentQuoteChar == nullChar)
            {
                if (IsQuotes(c))
                {
                    currentQuoteChar = c;
                }
                else if (splitPredicate(c))
                {
                    return resultIndex;
                }
            }
            else if (c == currentQuoteChar)
            {
                currentQuoteChar = nullChar;
            }

            resultIndex++;
        }

        return resultIndex;
    }

    public static List<string> SplitRespectQuotes(string input, Func<char, bool> splitPredicate, int resultInitialCapacity = 100)
    {
        List<string> result = new List<string>(resultInitialCapacity);

        int index = 0;

        while (index < input.Length)
        {
            int beginIndex = FindSubstringBeginIndex(input, index, splitPredicate);
            int endIndex = FindSubstringEndIndex(input, beginIndex, splitPredicate);

            if (beginIndex < input.Length)
            {
                int substringLength = endIndex - beginIndex;
                string substring = input.Substring(beginIndex, substringLength);
                string part = substring.Replace("\"", "").Replace("'", "");
                result.Add(part);
            }

            index = endIndex;
        }

        return result;
    }
}
