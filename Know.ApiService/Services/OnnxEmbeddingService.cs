using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Numerics.Tensors;

namespace Know.ApiService.Services;

public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text);
}

public class OnnxEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly string _modelPath;
    private readonly string _vocabPath;

    public OnnxEmbeddingService(IConfiguration configuration, IWebHostEnvironment env)
    {
        _modelPath = Path.Combine(env.ContentRootPath, "Models", "all-MiniLM-L6-v2.onnx");
        _vocabPath = Path.Combine(env.ContentRootPath, "Models", "vocab.txt");

        if (!File.Exists(_modelPath))
            throw new FileNotFoundException($"Model file not found at {_modelPath}");
        
        if (!File.Exists(_vocabPath))
            throw new FileNotFoundException($"Vocab file not found at {_vocabPath}");

        _session = new InferenceSession(_modelPath);
        _tokenizer = new BertTokenizer(_vocabPath);
    }

    public Task<float[]> GenerateEmbeddingAsync(string text)
    {
        return Task.Run(() =>
        {
            var (inputIds, attentionMask, tokenTypeIds) = _tokenizer.Tokenize(text);

            var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, inputIds.Length });
            var attentionMaskTensor = new DenseTensor<long>(attentionMask, new[] { 1, attentionMask.Length });
            var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, new[] { 1, tokenTypeIds.Length });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
            };

            using var results = _session.Run(inputs);
            
            // Get the last hidden state (usually the first output)
            // Shape: [BatchSize, SequenceLength, HiddenSize] -> [1, 256, 384]
            var lastHiddenState = results.First().AsTensor<float>();
            
            // Mean Pooling
            // We need to average the hidden states, but only for the tokens that are part of the attention mask
            var embedding = MeanPooling(lastHiddenState, attentionMask);

            // Normalize
            var normalized = Normalize(embedding);

            return normalized;
        });
    }

    private float[] MeanPooling(Microsoft.ML.OnnxRuntime.Tensors.Tensor<float> lastHiddenState, long[] attentionMask)
    {
        // lastHiddenState dimensions: [1, SequenceLength, HiddenSize]
        // attentionMask dimensions: [SequenceLength]
        
        int sequenceLength = lastHiddenState.Dimensions[1];
        int hiddenSize = lastHiddenState.Dimensions[2];
        
        var sum = new float[hiddenSize];
        int count = 0;

        for (int i = 0; i < sequenceLength; i++)
        {
            if (attentionMask[i] == 1)
            {
                for (int j = 0; j < hiddenSize; j++)
                {
                    sum[j] += lastHiddenState[0, i, j];
                }
                count++;
            }
        }

        for (int j = 0; j < hiddenSize; j++)
        {
            sum[j] /= Math.Max(count, 1);
        }

        return sum;
    }

    private float[] Normalize(float[] vector)
    {
        float sumSquares = 0;
        foreach (var val in vector)
        {
            sumSquares += val * val;
        }

        float norm = (float)Math.Sqrt(sumSquares);
        
        // Avoid division by zero
        if (norm == 0) return vector;

        var normalized = new float[vector.Length];
        for (int i = 0; i < vector.Length; i++)
        {
            normalized[i] = vector[i] / norm;
        }

        return normalized;
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
