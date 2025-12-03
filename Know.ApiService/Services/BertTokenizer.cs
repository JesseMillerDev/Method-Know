using System.Text;

namespace Know.ApiService.Services;

public class BertTokenizer
{
    private readonly Dictionary<string, int> _vocab;
    private readonly int _clsTokenId;
    private readonly int _sepTokenId;
    private readonly int _padTokenId;
    private readonly int _unkTokenId;

    public BertTokenizer(string vocabPath)
    {
        _vocab = new Dictionary<string, int>();
        var lines = File.ReadAllLines(vocabPath);
        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                _vocab[lines[i]] = i;
            }
        }

        _clsTokenId = _vocab.ContainsKey("[CLS]") ? _vocab["[CLS]"] : 101;
        _sepTokenId = _vocab.ContainsKey("[SEP]") ? _vocab["[SEP]"] : 102;
        _padTokenId = _vocab.ContainsKey("[PAD]") ? _vocab["[PAD]"] : 0;
        _unkTokenId = _vocab.ContainsKey("[UNK]") ? _vocab["[UNK]"] : 100;
    }

    public (long[] InputIds, long[] AttentionMask, long[] TokenTypeIds) Tokenize(string text, int maxLength = 256)
    {
        var tokens = new List<string>();
        tokens.Add("[CLS]");

        var normalized = text.ToLowerInvariant();
        // Simple whitespace tokenization for now, can be improved
        var words = normalized.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            var subTokens = WordPieceTokenize(word);
            tokens.AddRange(subTokens);
            if (tokens.Count >= maxLength - 1) break; // Reserve space for [SEP]
        }

        if (tokens.Count >= maxLength)
        {
            tokens = tokens.Take(maxLength - 1).ToList();
        }
        tokens.Add("[SEP]");

        var inputIds = new long[maxLength];
        var attentionMask = new long[maxLength];
        var tokenTypeIds = new long[maxLength];

        for (int i = 0; i < maxLength; i++)
        {
            if (i < tokens.Count)
            {
                var token = tokens[i];
                inputIds[i] = _vocab.TryGetValue(token, out var id) ? id : _unkTokenId;
                attentionMask[i] = 1;
            }
            else
            {
                inputIds[i] = _padTokenId;
                attentionMask[i] = 0;
            }
            tokenTypeIds[i] = 0; // Segment 0 for single sentence
        }

        return (inputIds, attentionMask, tokenTypeIds);
    }

    private List<string> WordPieceTokenize(string word)
    {
        var tokens = new List<string>();
        if (word.Length > 100)
        {
            tokens.Add("[UNK]");
            return tokens;
        }

        int start = 0;
        while (start < word.Length)
        {
            int end = word.Length;
            string? curSubStr = null;
            bool found = false;

            while (start < end)
            {
                var subStr = word.Substring(start, end - start);
                if (start > 0)
                {
                    subStr = "##" + subStr;
                }

                if (_vocab.ContainsKey(subStr))
                {
                    curSubStr = subStr;
                    found = true;
                    break;
                }
                end--;
            }

            if (!found)
            {
                tokens.Add("[UNK]");
                return tokens;
            }

            tokens.Add(curSubStr!);
            start = end;
        }

        return tokens;
    }
}
