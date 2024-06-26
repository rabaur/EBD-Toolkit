using System.Collections.Generic;
using System.IO;

namespace EBD
{
    public class IO
    {
        /// <summary>
        /// Writes a CSV file with the given column names and data.
        /// </summary>
        /// <param name="filePath">Path to write the file to.</param>
        /// <param name="columnNames">Names for each column.</param>
        /// <param name="data">
        /// List of lists corresponding to values for each row. Number of items in a row (length of inner lists) needs to
        /// match the number of names for columns (length of <paramref name="columnNames">
        /// </param>
        /// <param name="separator">The seperator used to delimit items in a row. Defaults to `;`.</param>
        /// <exception cref="System.Exception">
        /// Throws and exception if length of <paramref name="columnNames"/> does not match length of each element of
        /// <paramref name="data"/>
        /// </exception>
        public static void WriteCSV(
            string filePath,
            List<string> columnNames,
            List<List<string>> data,
            string separator = ";"
        )
        {
            // Check that the number of column names matches the number of columns in the data.
            foreach (List<string> row in data)
            {
                if (row.Count != columnNames.Count)
                {
                    throw new System.Exception($"Number of column names ({columnNames.Count}) does not match number of columns in data ({row.Count}).");
                }
            }

            using StreamWriter writer = new(filePath);
            // Write the column names.
            writer.WriteLine(string.Join(separator, columnNames.ToArray()));

            // Write the data.
            foreach (List<string> line in data)
            {
                writer.WriteLine(string.Join(separator, line.ToArray()));
            }
        }

        /// <summary>
        /// Reads from a CSV file.
        /// </summary>
        /// <param name="filePath">Path of the file.</param>
        /// <param name="separator">Separator used.</param>
        /// <returns>List of strings corresponding to column names and list of list of strings where each inner list
        /// corresponds to a row.</returns>
        public static (List<string>, List<List<string>>) ReadCSV(string filePath, string separator = ";")
        {
            List<string> columnNames = new();
            List<List<string>> data = new();

            using StreamReader reader = new(filePath);
            // Read the column names.
            string line = reader.ReadLine();
            columnNames.AddRange(line.Split(separator));

            // Read the data.
            while ((line = reader.ReadLine()) != null)
            {
                data.Add(new List<string>(line.Split(separator)));
            }

            return (columnNames, data);
        }

        public static string GenerateUniqueFilename(string dirName, string fileName)
        {
            // Create directory if does not exist.
            Directory.CreateDirectory(dirName);

            // This is the path the file will be written to.
            string path = Path.Combine(dirName, fileName);

            // Check if specified file exists yet and if user wants to overwrite.
            if (File.Exists(path))
            {
                /* In this case we need to make the filename unique.
                * We will achiece that by:
                * foldername + filename + extension -> foldername + filename + _x + extension
                * x will be increased in case of multiple overwrites.
                */
                string extension = Path.GetExtension(fileName);
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

                // Check if there was a previous overwrite and get highest identifier.
                int id = 0;
                while (File.Exists(Path.Combine(dirName, $"{fileNameWithoutExtension}_{id}{extension}")))
                {
                    id++;
                }

                // Now we have found a unique identifier and create the new name.
                path = Path.Combine(dirName, $"{fileNameWithoutExtension}_{id}{extension}");
            }
            return path;
        }
    }
}