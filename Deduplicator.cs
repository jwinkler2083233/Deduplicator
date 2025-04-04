using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;

namespace Trunade.Deduplicator
{
    public class Deduplicator
    {
        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool CreateHardLink(
            string lpFileName,
            string lpExistingFileName,
            IntPtr lpSecurityAttributes
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(
            IntPtr hFile,
            out BY_HANDLE_FILE_INFORMATION lpFileInformation
        );


        [StructLayout(LayoutKind.Sequential)]
        struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public FILETIME CreationTime;
            public FILETIME LastAccessTime;
            public FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }


        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CreateFile(
             [MarshalAs(UnmanagedType.LPTStr)] string filename,
             int access,
             int share,
             IntPtr securityAttributes, // optional SECURITY_ATTRIBUTES struct or IntPtr.Zero
             int creationDisposition,
             int flagsAndAttributes,
             IntPtr templateFile);


        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr hObject);

        /// <summary>
        /// this is a fast way to compare two buffer arrays in .net
        /// </summary>
        /// <param name="a1"></param>
        /// <param name="a2"></param>
        /// <param name="l"></param>
        /// <returns></returns>
        static unsafe bool UnsafeCompare(byte[] a1, byte[] a2, int l)
        {
            fixed (byte* p1 = a1, p2 = a2)
            {
                byte* x1 = p1, x2 = p2;

                for (int i = 0; i < l / 8; i++, x1 += 8, x2 += 8)
                    if (*((long*)x1) != *((long*)x2)) return false;

                if ((l & 4) != 0) { if (*((int*)x1) != *((int*)x2)) return false; x1 += 4; x2 += 4; }
                if ((l & 2) != 0) { if (*((short*)x1) != *((short*)x2)) return false; x1 += 2; x2 += 2; }
                if ((l & 1) != 0) if (*((byte*)x1) != *((byte*)x2)) return false;

                return true;
            }
        }

        /// <summary>
        /// this does a binary comparison of two files
        /// </summary>
        /// <param name="path1"></param>
        /// <param name="path2"></param>
        /// <returns></returns>
        static bool AreFilesSame(string path1, string path2)
        {
            try
            {
                FileInfo info1 = new FileInfo(path1);
                FileInfo info2 = new FileInfo(path2);

                if (info1.Length != info2.Length)
                    return false;
            }
            catch (PathTooLongException pe)
            {
                Debug.WriteLine(pe.Message);
                return false;
            }

            // allocate an array for each file
            byte[] buf1 = new byte[16384];
            byte[] buf2 = new byte[16384];

            // Open the two files.
            using (FileStream fs1 = File.OpenRead(path1))
            using (FileStream fs2 = File.OpenRead(path2))
            {

                BufferedStream bs1 = new BufferedStream(fs1);
                BufferedStream bs2 = new BufferedStream(fs2);

                while (true)
                {
                    int nRead1 = bs1.Read(buf1, 0, 16384);
                    int nRead2 = bs2.Read(buf2, 0, 16384);

                    if (nRead1 != nRead2)
                        return false;

                    if (nRead1 <= 0)
                        break;

                    // binary comparison
                    if (UnsafeCompare(buf1, buf2, nRead1) == false)
                        return false;
                }
            }

            return true;
        }


        /// <summary>
        /// this is the entry point for the program
        /// </summary>
        public static void Main(string[] args)
        {
            Console.WriteLine("Deduplicator v1.0,  Copyright 2015  Trunade Solutions, Inc.");
            Console.WriteLine("All rights reserved.  There is no warranty, express or implied, with this program.");

            Console.WriteLine("");
            if(args.Length == 0)
            {
                Console.WriteLine("Usage:  Deduplicator <path of directory to start search>");
                return;
            }
            Console.WriteLine("Deduplicating files, starting in " + args[0]);

            string startingDir = args[0];

            Dictionary<string, string> dictFiles = new Dictionary<string, string>();

            try
            {
                SearchAndInsert(startingDir, dictFiles);
            } catch(Exception e)
            {
                Debug.WriteLine(e.Message);
            }
        }

        /// <summary>
        /// this method is recursive, and will iterate over the directories
        /// </summary>
        /// <param name="dirStart"></param>
        /// <param name="dictFiles"></param>
        public static void SearchAndInsert(string dirStart, Dictionary<string, string> dictFiles)
        {
            string[] dirs = null;
            try
            {
                dirs = Directory.GetDirectories(dirStart);
            }
            catch (PathTooLongException pe)
            {
                Debug.WriteLine(pe.Message);
                return;
            }

            foreach (string dir in dirs)
            {
                SearchAndInsert(dir, dictFiles);
            }

            string[] files = Directory.GetFiles(dirStart);

            foreach (string file in files)
            {
                string filename = Path.GetFileName(file);

                // check here for prior existence of the file
                if (dictFiles.ContainsKey(filename))
                {
                    string file2 = dictFiles[filename];
                    if (AreFilesSame(file, file2))
                    {
                        if(!AreFilesPointingToSameFile(file, file2))
                        {
                            try
                            {
                                Console.WriteLine("Removing and linking: " + file);
                                File.Delete(file);
                            } catch(Exception e)
                            {
                                Debug.WriteLine(e.Message);
                                Console.WriteLine(e.Message);
                                continue;
                            }
                            if(CreateHardLink(file, file2, IntPtr.Zero) == false)
                            {
                                Debug.WriteLine("Error creating hard link");
                                Console.WriteLine("Error creating link");
                                File.Copy(file2, file);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Pointing to same file: " + file + "       " + file2);
                        }
                        Debug.WriteLine(file);
                    }
                }
                else
                {
                    dictFiles.Add(Path.GetFileName(file), file);
                }
            }
        }

        /// <remarks>This function uses BY_HANDLE_FILE_INFORMATION. This is the only way to determine authoritatively
        /// whether two filenames are equivalent. MSDN explains:
        /// The identifier (low and high parts) and the volume serial number uniquely identify a file on a single computer.
        /// To determine whether two open handles represent the same file, combine the identifier and the volume serial number
        /// for each file and compare them.</remarks>
        static bool AreFilesPointingToSameFile(string fileName1, string fileName2)
        {
            int desiredAccess = 0;  // ' We request neither read nor write access
            int fileShareMode = 7;  //  ' FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE
            int createDisposition = 3;  // ' OPEN_EXISTING
            int fileAttributes = 0x2000080;     // ' FILE_FLAG_BACKUP_SEMANTICS|FILE_ATTRIBUTES_NORMAL. BACKUP is needed to open a directory.

            IntPtr handle1 = CreateFile(fileName1, desiredAccess, fileShareMode, IntPtr.Zero, createDisposition, fileAttributes, IntPtr.Zero);
            IntPtr handle2 = CreateFile(fileName2, desiredAccess, fileShareMode, IntPtr.Zero, createDisposition, fileAttributes, IntPtr.Zero);

            IntPtr invalidHandle = new IntPtr(-1);
            if (handle1 == invalidHandle ||
                handle2 == invalidHandle)
            {
                if (handle1 != invalidHandle)
                    CloseHandle(handle1);

                if (handle2 != invalidHandle)
                    CloseHandle(handle2);

                return false;
            }

            BY_HANDLE_FILE_INFORMATION info1 = new BY_HANDLE_FILE_INFORMATION();
            BY_HANDLE_FILE_INFORMATION info2 = new BY_HANDLE_FILE_INFORMATION();

            bool r1 = GetFileInformationByHandle(handle1, out info1);
            bool r2 = GetFileInformationByHandle(handle2, out info2);

            CloseHandle(handle1);
            CloseHandle(handle2);

            if (!r1 || !r2)
                return false;

            return (info1.FileIndexLow == info2.FileIndexLow &&
                info1.FileIndexHigh == info2.FileIndexHigh &&
                    info1.VolumeSerialNumber == info2.VolumeSerialNumber);
        }

    }
}
