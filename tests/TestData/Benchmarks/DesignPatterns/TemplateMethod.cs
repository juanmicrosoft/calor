using System;

namespace DesignPatterns
{
    /// <summary>
    /// Template Method Pattern: Defines the skeleton of an algorithm in an operation,
    /// deferring some steps to subclasses.
    /// </summary>
    public abstract class DataProcessor
    {
        /// <summary>
        /// Template method that defines the processing algorithm.
        /// </summary>
        public string Process()
        {
            var data = ReadData();
            if (!ValidateData(data))
            {
                return "Validation failed";
            }
            var processed = ProcessData(data);
            return SaveResults(processed);
        }

        /// <summary>
        /// Abstract step: Read data from source.
        /// </summary>
        protected abstract string ReadData();

        /// <summary>
        /// Abstract step: Process the data.
        /// </summary>
        protected abstract string ProcessData(string data);

        /// <summary>
        /// Abstract step: Save the results.
        /// </summary>
        protected abstract string SaveResults(string data);

        /// <summary>
        /// Virtual hook: Validate the data. Can be overridden by subclasses.
        /// </summary>
        protected virtual bool ValidateData(string data)
        {
            return !string.IsNullOrEmpty(data);
        }
    }

    /// <summary>
    /// Concrete implementation for CSV data processing.
    /// </summary>
    public class CsvProcessor : DataProcessor
    {
        private readonly string _inputPath;
        private readonly string _outputPath;

        public CsvProcessor(string inputPath, string outputPath)
        {
            _inputPath = inputPath;
            _outputPath = outputPath;
        }

        protected override string ReadData()
        {
            return $"CSV data from {_inputPath}";
        }

        protected override string ProcessData(string data)
        {
            // Simulate CSV parsing and transformation
            return data.Replace(",", "|");
        }

        protected override string SaveResults(string data)
        {
            return $"Saved CSV to {_outputPath}: {data}";
        }

        protected override bool ValidateData(string data)
        {
            // CSV-specific validation: check for proper format
            return base.ValidateData(data) && data.Contains("CSV");
        }
    }

    /// <summary>
    /// Concrete implementation for JSON data processing.
    /// </summary>
    public class JsonProcessor : DataProcessor
    {
        private readonly string _endpoint;
        private readonly string _outputPath;

        public JsonProcessor(string endpoint, string outputPath)
        {
            _endpoint = endpoint;
            _outputPath = outputPath;
        }

        protected override string ReadData()
        {
            return $"{{\"source\": \"{_endpoint}\", \"data\": \"JSON content\"}}";
        }

        protected override string ProcessData(string data)
        {
            // Simulate JSON transformation
            return data.Replace("JSON", "PROCESSED_JSON");
        }

        protected override string SaveResults(string data)
        {
            return $"Saved JSON to {_outputPath}: {data}";
        }

        // Uses default ValidateData implementation
    }

    /// <summary>
    /// Helper class to demonstrate the Template Method pattern.
    /// </summary>
    public static class TemplateMethodDemo
    {
        public static string ProcessCsv(string input, string output)
        {
            var processor = new CsvProcessor(input, output);
            return processor.Process();
        }

        public static string ProcessJson(string endpoint, string output)
        {
            var processor = new JsonProcessor(endpoint, output);
            return processor.Process();
        }
    }
}
