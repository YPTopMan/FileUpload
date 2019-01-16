using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;


namespace WebUpload.Controllers
{
    /// <summary>
    /// 文件上传
    /// </summary>
    public class UploadController : Controller
    {

        private readonly IHostingEnvironment _hostingEnvironment;
        public UploadController(IHostingEnvironment hostingEnvironment)
        {
            _hostingEnvironment = hostingEnvironment;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public ActionResult Index()
        {
            return View();
        }

        #region  文件上传(包含切片)

        /// <summary>
        /// 获取指定文件的已上传的最大文件块
        /// </summary>
        /// <param name="md5">文件唯一值</param>
        /// <param name="ext">文件后缀</param>
        /// <returns></returns>
        [HttpGet]
        public JsonResult GetMaxChunk(string md5, string ext)
        {
            try
            {
                var result = JResult.Success();

                // 检测文件夹是否存在，不存在则创建
                var userPath = GetPath();
                if (!Directory.Exists(userPath))
                {
                    DicCreate(userPath);
                }

                var md5Folder = getFileMD5Folder(md5);
                if (!Directory.Exists(md5Folder))
                {
                    DicCreate(md5Folder);
                    return Json(result);
                }

                var fileName = md5 + "." + ext;
                string targetPath = Path.Combine(md5Folder, fileName);
                // 文件已经存在，则可能存在问题，直接删除，重新上传
                if (System.IO.File.Exists(targetPath))
                {
                    System.IO.File.Delete(targetPath);
                    return Json(result);
                }

                DirectoryInfo dicInfo = new DirectoryInfo(md5Folder);
                var files = dicInfo.GetFiles();
                var chunk = files.Count();
                if (chunk > 1)
                {
                    //当文件上传中时，页面刷新，上传中断，这时最后一个保存的块的大小可能会有异常，所以这里直接删除最后一个块文件                  
                    result.Data = (chunk - 1);
                    return Json(result);
                }

                return Json(result);
            }
            catch (Exception ex)
            {
                var errMsg = ex.Message;
                return Json(JResult.Error(errMsg));
            }
        }

        /// <summary>
        /// 文件分块上传
        /// </summary>
        /// <param name="file">文件</param>
        /// <param name="md5">文件md5 值</param>
        /// <param name="chunk">当前分片在上传分片中的顺序（从0开始）</param>
        /// <param name="chunks">最大片数</param>
        /// <returns></returns>
        public JsonResult ChunkUpload(IFormFile file, string md5, int? chunk, int chunks = 0)
        {
            try
            {
                var jsucess = JResult.Success();
                var md5Folder = getFileMD5Folder(md5);
                string filePath = "";  // 要保存的文件路径

                // 存在分片参数,并且，最大的片数大于1片时     
                if (chunk.HasValue && chunks > 1)
                {
                    var uploadNumsOfLoop = 10;
                    // 是10的倍数就休眠几秒（数据库设置的秒数）
                    if (chunk % uploadNumsOfLoop == 0)
                    {
                        var timesOfLoop = 10;   //休眠毫秒,可从数据库取值
                        Thread.Sleep(timesOfLoop);
                    }
                    //建立临时传输文件夹
                    if (!Directory.Exists(md5Folder))
                    {
                        Directory.CreateDirectory(md5Folder);
                    }

                    filePath = md5Folder + "/" + chunk;
                    jsucess.Code = chunk.Value;
                    if (chunks == chunk)
                    {
                        jsucess.Message = "chunked";
                    }
                }
                else
                {
                    var fileName = file?.FileName;
                    if (string.IsNullOrEmpty(fileName))
                    {
                        var fileNameQuery = Request.Query.FirstOrDefault(t => t.Key == "name");
                        fileName = fileNameQuery.Value.FirstOrDefault();
                    }

                    //没有分片直接保存
                    filePath = md5Folder + Path.GetExtension(fileName);
                    jsucess.Message = "chunked";
                }


                // 写入文件
                using (var addFile = new FileStream(filePath, FileMode.OpenOrCreate))
                {
                    if (file != null)
                    {
                        file.CopyTo(addFile);
                    }
                    else
                    {
                        Request.Body.CopyTo(addFile);
                    }
                }

                return Json(jsucess);
            }
            catch (Exception ex)
            {
                return Json(JResult.Error(ex.Message));
            }
        }

        /// <summary>
        /// 合并文件
        /// </summary>
        /// <returns></returns>
        public JsonResult MergeFiles(string md5, string filename)
        {
            try
            {
                //源数据文件夹
                string sourcePath = getFileMD5Folder(md5);
                //合并后的文件路径
                string targetFilePath = sourcePath + Path.GetExtension(filename);
                // 目标文件不存在，则需要合并
                if (!System.IO.File.Exists(targetFilePath))
                {
                    if (!Directory.Exists(sourcePath))
                    {
                        return Json(JResult.Error("未找到对应的文件片"));
                    }

                    MergeDiskFile(sourcePath, targetFilePath);
                }

                var vaild = VaildMergeFile(targetFilePath);
                DeleteFolder(sourcePath);
                if (!vaild.Result)
                {
                    return Json(vaild);
                }              
                var fileResult = OwnBusiness(targetFilePath);
                return Json(JResult.Success());
            }
            catch (Exception ex)
            {
                return Json(JResult.Error(ex.Message));
            }
        }

        /// <summary>
        /// 获得上传文件目录
        /// </summary>
        /// <returns></returns>
        private string GetPath()
        {
            var webRootPath = _hostingEnvironment.WebRootPath;
            return webRootPath + "\\Files\\";
        }

        /// <summary>
        /// 获得文件MD5文件夹
        /// </summary>
        /// <returns></returns>
        private string getFileMD5Folder(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                throw new Exception("缺少文件MD5值");
            }

            string root = GetPath();

            string md5Folder = "";
            md5Folder = root + "ChunkTemp\\" + identifier;
            return md5Folder;
        }

        /// <summary>
        /// 将磁盘上的切片源合并成一个文件
        /// <returns>返回所有切片文件的字节总和</returns>
        /// </summary>
        /// <param name="sourcePath">磁盘上的切片源</param>
        /// <param name="targetPath">目标文件路径</param>
        private int MergeDiskFile(string sourcePath, string targetPath)
        {
            FileStream addFile = null;
            BinaryWriter addWriter = null;
            try
            {
                int streamTotalSize = 0;
                addFile = new FileStream(targetPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                //addFile = new FileStream(targetPath, FileMode.Append, FileAccess.Write);
                addWriter = new BinaryWriter(addFile);
                // 获取目录下所有的切片文件块
                FileInfo[] files = new DirectoryInfo(sourcePath).GetFiles();
                // 按照文件名(数字)进行排序
                var orderFileInfoList = files.OrderBy(f => int.Parse(f.Name));
                foreach (FileInfo diskFile in orderFileInfoList)
                {
                    //获得上传的分片数据流 
                    Stream stream = diskFile.Open(FileMode.Open);
                    BinaryReader tempReader = new BinaryReader(stream);
                    var streamSize = (int)stream.Length;
                    //将上传的分片追加到临时文件末尾
                    addWriter.Write(tempReader.ReadBytes(streamSize));
                    streamTotalSize += streamSize;
                    //关闭BinaryReader文件阅读器
                    tempReader.Close();
                    stream.Close();

                    tempReader.Dispose();
                    stream.Dispose();
                }
                addWriter.Close();
                addFile.Close();
                addWriter.Dispose();
                addFile.Dispose();
                return streamTotalSize;
            }
            catch (Exception ex)
            {
                if (addFile != null)
                {
                    addFile.Close();
                    addFile.Dispose();
                }

                if (addWriter != null)
                {
                    addWriter.Close();
                    addWriter.Dispose();
                }
                throw ex;
            }
        }

        /// <summary>
        /// 校验合并后的文件
        /// <para>1.是否没有漏掉块(chunk)</para>
        /// <para>2.检测文件大小是否跟客户端一样</para>
        /// <para>3.检查文件的MD5值是否一致</para>
        /// </summary>
        /// <param name="targetPath"></param>
        /// <returns></returns>
        private CommResult VaildMergeFile(string targetPath)
        {
            var clientFileName = Request.Form["filename"];

            // 文件字节总数
            var fileTotalSize = Convert.ToInt32(Request.Form["fileTotalSize"]);

            var targetFile = new FileInfo(targetPath);
            var streamTotalSize = targetFile.Length;
            try
            {
                if (streamTotalSize != fileTotalSize)
                {
                    throw new Exception("[" + clientFileName + "]文件上传时发生损坏，请重新上传");
                }

                // 对文件进行 MD5 唯一验证
                var identifier = Request.Form["md5"];
                var fileMD5 = GetMD5HashFromFile(targetPath);
                if (!fileMD5.Equals(identifier))
                {
                    throw new Exception("[" + clientFileName + "],文件MD5值不对等");
                }
                return JResult.Success();
            }
            catch (Exception ex)
            {
                // 删除本地错误文件
                System.IO.File.Delete(targetPath);
                return JResult.Error(ex.Message);
            }

        }

        /// <summary>
        /// C#获取文件MD5值方法
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private string GetMD5HashFromFile(string fileName)
        {
            try
            {
                FileStream file = new FileStream(fileName, FileMode.Open);
                System.Security.Cryptography.MD5 md5 = new System.Security.Cryptography.MD5CryptoServiceProvider();
                byte[] retVal = md5.ComputeHash(file);
                file.Close();

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < retVal.Length; i++)
                {
                    sb.Append(retVal[i].ToString("x2"));
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                throw new Exception("GetMD5HashFromFile() fail,error:" + ex.Message);
            }
        }

        /// <summary>
        /// 删除文件夹及其内容
        /// <para>附带删除超过一个月的文件以及文件夹</para>
        /// </summary>
        /// <param name="strPath"></param>
        private void DeleteFolder(string strPath)
        {
            if (Directory.Exists(strPath))
                Directory.Delete(strPath, true);

            #region 删除一个月以前的临时文件夹与文件
            var chunkTemp = Path.GetDirectoryName(strPath);
            DirectoryInfo dir = new DirectoryInfo(chunkTemp);
            DirectoryInfo[] dii = dir.GetDirectories();
            // 超过一个月的文件夹和文件
            var expireDate = DateTime.Now.AddMonths(-1);
            var deleteExpire = dii.Where(t => t.LastWriteTime < expireDate).ToList();
            if (deleteExpire.Any())
            {
                foreach (var item in deleteExpire)
                {
                    Directory.Delete(chunkTemp + "/" + item, true);
                }
            }

            var deleteExpireFile = dir.GetFiles().Where(t => t.LastWriteTime < expireDate).ToList();
            if (deleteExpireFile.Any())
            {
                foreach (var item in deleteExpireFile)
                {
                    System.IO.File.Delete(chunkTemp + "/" + item);
                }
            }
            #endregion
        }

        /// <summary>
        /// 文件上传后调用自有业务 
        /// </summary>
        /// <param name="targetPath"></param>
        /// <returns>返回文件对象</returns>
        private CommResult OwnBusiness(string targetPath)
        {
            var fileResult = JResult.Success();

            // 移动文件            
            var file = new FileInfo(targetPath);
            var newFilePath = _hostingEnvironment.ContentRootPath + "\\Files\\" + Request.Form["filename"];
            if (System.IO.File.Exists(newFilePath))
            {
                System.IO.File.Delete(newFilePath);
            }
            file.MoveTo(newFilePath);

            return fileResult;
        }

        #endregion

        /// <summary>
        /// 文件目录如果不存在，就创建一个新的目录
        /// </summary>
        /// <param name="path"></param>
        private void DicCreate(string path)
        {
            if (!System.IO.Directory.Exists(path))
            {
                System.IO.Directory.CreateDirectory(path);
            }
        }

    }

}
