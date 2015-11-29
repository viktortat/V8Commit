﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using V8Commit.Entities.V8FileSystem;

namespace V8Commit.Services.FileV8Services
{
    public class FileV8Reader : IDisposable
    {
        private bool disposed = false;

        private readonly string _fileName;
        private BinaryReader _reader;

        public FileV8Reader(string fileName)
        {
            if (File.Exists(fileName))
            {
                this._fileName = fileName;
                _reader = new BinaryReader(File.Open(_fileName, FileMode.Open));
            }
            else
            {
                throw new NotImplementedException();
            }
        }
        public FileV8Reader(BinaryReader reader)
        {
            if (reader != null)
            {
                _reader = reader;
            }
            else
            {
                throw new NotImplementedException();
            }
        }
        ~FileV8Reader()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    if (_reader != null)
                    {
                        _reader.Dispose();
                        _reader = null;
                    }
                }

                disposed = true;
            }
        }

        public V8FileSystem ReadV8FileSystem(bool isInflated = true)
        {
            V8FileSystem fileSystem = new V8FileSystem();

            // Reading container 16 bytes
            fileSystem.Container = ReadContainerHeader();

            // Reading container references
            fileSystem.References = ReadFileSystemReferences(isInflated);

            return fileSystem;
        }
        public V8FileSystem ReadHeadersV8FileSystem()
        {
            V8FileSystem fileSystem = new V8FileSystem();

            // Reading container 16 bytes
            fileSystem.Container = ReadContainerHeader();

            // Reading block header 31 bytes
            fileSystem.BlockHeader = ReadBlockHeader();

            return fileSystem;
        }
        public void Seek(Int32 offset, SeekOrigin origin)
        {
            _reader.BaseStream.Seek(offset, origin);
        }
        public bool IsV8FileSystem(MemoryStream memStream, Int32 fileSize)
        {
            if (fileSize < V8ContainerHeader.Size() + V8BlockHeader.Size())
            {
                return false;
            }

            memStream.Seek(0, SeekOrigin.Begin);
            using (FileV8Reader v8Reader = new FileV8Reader(new BinaryReader(memStream, Encoding.Default, true)))
            {
                try
                {
                    v8Reader.ReadHeadersV8FileSystem();
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }
        // TODO : make as plugins
        public void WriteToOutputDirectory(FileV8Reader fileV8Reader, V8FileSystem fileSystem, string outputDirectory)
        {
            foreach (var reference in fileSystem.References)
            {
                fileV8Reader.Seek(reference.RefToData, SeekOrigin.Begin);
                string path = outputDirectory + reference.FileHeader.FileName;

                using (MemoryStream memStream = new MemoryStream())
                {
                    using (MemoryStream memReader = new MemoryStream(ReadBytes(ReadBlockHeader())))
                    {
                        if (reference.IsInFlated)
                        {
                            using (DeflateStream deflateStream = new DeflateStream(memReader, CompressionMode.Decompress))
                            {
                                deflateStream.CopyTo(memStream);
                            }
                        }
                        else
                        {
                            memReader.CopyTo(memStream);
                        }
                    }

                    if (fileV8Reader.IsV8FileSystem(memStream, memStream.Capacity))
                    {
                        if (!Directory.Exists(path))
                        {
                            Directory.CreateDirectory(path);
                        }

                        memStream.Seek(0, SeekOrigin.Begin);
                        using (FileV8Reader v8Reader = new FileV8Reader(new BinaryReader(memStream, Encoding.Default, true)))
                        {
                            reference.Folder = v8Reader.ReadV8FileSystem(false);
                            WriteToOutputDirectory(v8Reader, reference.Folder, path + "\\");
                        }
                    }
                    else
                    {
                        using (FileStream fileStream = File.Create(path))
                        {
                            memStream.Seek(0, SeekOrigin.Begin);
                            memStream.CopyTo(fileStream);
                        }
                    }
                }
            }
        }
        private V8ContainerHeader ReadContainerHeader()
        {
            V8ContainerHeader container = new V8ContainerHeader();
            container.RefToNextPage = _reader.ReadInt32();
            container.PageSize = _reader.ReadInt32();
            container.PagesCount = _reader.ReadInt32();
            container.ReservedField = _reader.ReadInt32();

            return container;
        }
        private V8BlockHeader ReadBlockHeader()
        {
            char[] Block = _reader.ReadChars(V8BlockHeader.Size());
            if (Block[0] != 0x0d || Block[1] != 0x0a ||
                Block[10] != 0x20 || Block[19] != 0x20 ||
                Block[28] != 0x20 || Block[29] != 0x0d || Block[30] != 0x0a)
            {
                throw new NotImplementedException();
            }

            string HexDataSize = new string(Block, 2, 8);
            string HexPageSize = new string(Block, 11, 8);
            string HexNextPage = new string(Block, 20, 8);

            V8BlockHeader header = new V8BlockHeader();
            header.DataSize = Convert.ToInt32(HexDataSize, 16);
            header.PageSize = Convert.ToInt32(HexPageSize, 16);
            header.RefToNextPage = Convert.ToInt32(HexNextPage, 16);

            return header;
        }
        private V8FileHeader ReadFileHeader(Int32 dataSize)
        {
            V8FileHeader fileHeader = new V8FileHeader();
            fileHeader.CreationDate = _reader.ReadUInt64();
            fileHeader.ModificationDate = _reader.ReadUInt64();
            fileHeader.ReservedField = _reader.ReadInt32();

            string fileName = new string(_reader.ReadChars(dataSize - V8FileHeader.Size()));
            fileHeader.FileName = fileName.Replace("\0", string.Empty);

            return fileHeader;
        }
        private V8FileSystemReference ReadFileSystemReference(byte[] buffer, int position)
        {
            V8FileSystemReference reference = new V8FileSystemReference();
            reference.RefToHeader = BitConverter.ToInt32(buffer, position);
            reference.RefToData = BitConverter.ToInt32(buffer, position + 4);
            reference.ReservedField = BitConverter.ToInt32(buffer, position + 8);

            return reference;
        }
        private List<V8FileSystemReference> ReadFileSystemReferences(bool isInflated = true)
        {
            V8BlockHeader blockHeader = ReadBlockHeader();
            Int32 dataSize = blockHeader.DataSize;
            Int32 capacity = (int)(dataSize / V8FileSystemReference.Size());
            byte[] bytes = ReadBytes(blockHeader);

            List<V8FileSystemReference> references = new List<V8FileSystemReference>(capacity);

            Int32 bytesReaded = 0;
            while (dataSize > bytesReaded)
            {
                V8FileSystemReference reference = ReadFileSystemReference(bytes, bytesReaded);

                // Seek to reference block header 
                Seek(reference.RefToHeader, SeekOrigin.Begin);
                reference.FileHeader = ReadFileHeader(ReadBlockHeader().DataSize);
                reference.IsInFlated = isInflated;
                references.Add(reference);

                bytesReaded += V8FileSystemReference.Size();
            }

            return references;
        }
        private byte[] ReadBytes(V8BlockHeader blockHeader)
        {
            Int32 bytesReaded = 0;
            Int32 bytesToRead = 0;
            Int32 dataSize = blockHeader.DataSize;
            byte[] bytes = new byte[dataSize];

            while (dataSize > bytesReaded)
            {
                bytesToRead = Math.Min(blockHeader.PageSize, dataSize - bytesReaded);
                _reader.Read(bytes, bytesReaded, bytesToRead);
                bytesReaded += bytesToRead;
                if (blockHeader.RefToNextPage != 0x7FFFFFFF)
                {
                    // Seek to next block header
                    Seek(blockHeader.RefToNextPage, SeekOrigin.Begin);
                    blockHeader = ReadBlockHeader();
                }
            }

            return bytes;
        }
    }
}
