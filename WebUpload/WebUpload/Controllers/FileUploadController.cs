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
    public class FileUploadController : Controller
    {

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
                var reult = JResult.Success();

                // 检测文件夹是否存在，不存在则创建
                var userPath = GetPath();
                if (!Directory.Exists(userPath))
                {
                    DicCreate(userPath);
                }

                var md5Folder = getFileMD5Folder();
                if (!Directory.Exists(md5Folder))
                {
                    DicCreate(md5Folder);
                    return Json(reult, JsonRequestBehavior.AllowGet);
                }

                var fileName = md5 + "." + ext;
                string targetPath = Path.Combine(md5Folder, fileName);
                // 文件已经存在，则可能存在问题，直接删除，重新上传
                if (System.IO.File.Exists(targetPath))
                {
                    System.IO.File.Delete(targetPath);
                    return Json(reult, JsonRequestBehavior.AllowGet);
                }

                DirectoryInfo dicInfo = new DirectoryInfo(md5Folder);
                var files = dicInfo.GetFiles();
                var chunk = files.Count();
                if (chunk > 1)
                {
                    //当文件上传中时，页面刷新，上传中断，这时最后一个保存的块的大小可能会有异常，所以这里直接删除最后一个块文件                  
                    reult.Data = (chunk - 1);
                    return Json(reult, JsonRequestBehavior.AllowGet);
                }

                return Json(reult, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                var errMsg = ex.Message;
                return Json(JResult.Error(errMsg), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// 文件分块上传
        /// </summary>
        /// <param name="file">文件</param>
        /// <param name="chunk">当前分片在上传分片中的顺序（从0开始）</param>
        /// <returns></returns>
        public JsonResult ChunkUpload(HttpPostedFileBase file, int chunk = 0)
        {
            try
            {
                var md5Folder = getFileMD5Folder();
                // 存在分片参数
                if (Request.Form.AllKeys.Any(t => t == "chunk"))
                {
                    var webAppUpNumsOfLoop = HMSUtil.GetConfig(EnuInitCode.WebAppUpNumsOfLoop.ToString()).ToInt(10);
                    // 是10的倍数就休眠几秒（数据库设置的秒数）
                    if (chunk % webAppUpNumsOfLoop == 0)
                    {
                        var webAppUpPauseTimesOfLoop = HMSUtil.GetConfig(EnuInitCode.WebAppUpPauseTimesOfLoop.ToString()).ToInt(10);
                        Thread.Sleep(webAppUpPauseTimesOfLoop);
                    }

                    //建立临时传输文件夹
                    if (!Directory.Exists(md5Folder))
                    {
                        Directory.CreateDirectory(md5Folder);
                    }

                    var chunkFilePath = md5Folder + "/" + chunk;
                    if (file != null)
                    {
                        file.SaveAs(chunkFilePath);
                    }
                    else
                    {
                        byte[] byts = new byte[Request.InputStream.Length];
                        Request.InputStream.Read(byts, 0, byts.Length);
                        var createChunkFile = System.IO.File.Create(chunkFilePath);
                        createChunkFile.Write(byts, 0, byts.Length);
                        createChunkFile.Close();
                    }
                    var jsucess = JResult.Success();
                    jsucess.Code = chunk.ToStr();
                    // 当前片数等于总片数时，则返回切片完成的标识
                    var chunks = Request.Form["chunks"].ToInt();
                    if ((chunks - 1) == chunk)
                    {
                        jsucess.Message = "chunked";
                    }
                    return Json(jsucess, JsonRequestBehavior.AllowGet);
                }
                else
                {
                    //没有分片直接保存
                    string path = md5Folder + Path.GetExtension(file.FileName);
                    file.SaveAs(path);
                    return Json(JResult.Success("chunked"));
                }
            }
            catch (Exception ex)
            {
                return Json(JResult.Error(ex.Message), JsonRequestBehavior.AllowGet);
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
                string sourcePath = getFileMD5Folder();
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
                if (!vaild.Result)
                {
                    return Json(vaild);
                }
                DeleteFolder(sourcePath);
                FileUploadInfo fileResult = OwnBusiness(targetFilePath);
                return Json(JResult.Success(fileResult));
            }
            catch (Exception ex)
            {
                return Json(JResult.Error(ex.Message));
            }
        }

        /// <summary>
        /// 获得切片上传唯一文件夹
        /// </summary>
        /// <returns></returns>
        private string GetDocChunkFileFolder()
        {
            var userId = AMSAuthentication.GetAuthToken().UserId;
            string identifier = Request["md5"];
            string rentDoccode = Request["rentDoccode"];
            string rentDocProp = Request["rentDocProp"];
            string rentdocType = Request["rentdocType"];
            string rentdoctbDetailId = Request["rentdoctbDetailId"];

            // 用户编号_数字档案主键_数字档案单据码_数字档案类型_数字档案类型2_MD5
            return userId + "_" + rentdoctbDetailId + "_" + rentDoccode + "_" + rentDocProp + "_" + rentdocType + "_" + identifier;
        }

        /// <summary>
        /// 获得工作流切片上传唯一文件夹
        /// </summary>
        /// <returns></returns>
        private string GetFlowChunkFileFolder()
        {
            string identifier = Request["md5"];
            string flowId = Request["flowId"];
            string tempDirName = Request["tempDirName"];
            if (string.IsNullOrEmpty(tempDirName))
            {
                tempDirName = HMSUtil.GetFlowDirName();
            }
            var userId = AMSAuthentication.GetAuthToken().UserId;
            tempDirName = tempDirName.Replace("\\", "");
            // 用户编号_工作流编号_MD5
            return userId + "_" + flowId + "_" + identifier;
        }


        /// <summary>
        /// 获得附件切片上传唯一文件夹 
        /// </summary>
        /// <returns></returns>
        private string GetAccessoryChunkFileFolder()
        {
            string identifier = Request["md5"];
            var locationid = Request["locationid"];
            var location = Request["location"];
            var userId = AMSAuthentication.GetAuthToken().UserId;
            // 用户编号_工作流编号_MD5
            return userId + "_" + location + "_" + locationid + "_" + identifier;
        }

        /// <summary>
        /// 获得上传文件目录
        /// </summary>
        /// <returns></returns>
        private string GetPath()
        {
            var selfCode = AMSAuthentication.GetAuthToken().SelfCode;
            return HMSUtil.GetConfig(EnuInitCode.UpImageDirectory.ToString()) + "\\" + selfCode + "\\";
        }

        /// <summary>
        /// 获得文件MD5文件夹
        /// </summary>
        /// <returns></returns>
        private string getFileMD5Folder()
        {
            string identifier = Request["md5"];
            if (string.IsNullOrEmpty(identifier))
            {
                throw new Exception("缺少文件MD5值");
            }

            string root = GetPath();

            string md5Folder = "";
            // 根据业务类型生成对用的临时文件夹
            var pageType = Request["pagetype"];
            switch (pageType.ToLower())
            {
                case "flow":  // 工作流
                    md5Folder = root + "FlowChunkTemp\\" + GetFlowChunkFileFolder();
                    break;
                case "rent_doc_detail":  // 数字档案
                    md5Folder = root + "ChunkTemp\\" + GetDocChunkFileFolder();
                    break;
                default:      // 其他
                    md5Folder = root + "ChunkTemp\\" + GetAccessoryChunkFileFolder();
                    break;
            }
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
        private CommonResult VaildMergeFile(string targetPath)
        {
            var clientFileName = Request.Form["filename"];

      
            // 文件字节总数
            var fileTotalSize = Request.Form["fileTotalSize"];

            var targetFile = new FileInfo(targetPath);
            var streamTotalSize = targetFile.Length;

            if (streamTotalSize != fileTotalSize)
            {
                // 删除本地错误文件
                System.IO.File.Delete(targetPath);
                var errorResult = JResult.Error("[" + clientFileName + "]文件上传时发生损坏，请重新上传");

                #region 记录日志

                //BzfLogger.Log(new SysLog("切片上传-出错-数字档案编码：" + rentDoccode + " 数字档案详情编号: " + rentdoctbDetailId + "  错误信息见详情",
                //        "WebAPP.Api.Flow.RentDocController", errorResult,
                //        "请求路径：" + Request.Url.AbsolutePath));

                #endregion
                return errorResult;

            }

            // 3.判断上传完的最终的文件是否跟客户端大小一致,只是双保险        
            if (fileTotalSize != streamTotalSize)
            {
                var errorResult = JResult.Error("[" + clientFileName + "],文件大小跟上传文件的大小不匹配");
                //HMSLogger.Log(new SysLog("合并-出错-文件大小不一致-见错误详情", "WebAPP.Api.Flow.RentDocController",
                //   errorResult, "请求路径：" + Request.Url.AbsolutePath));

                return errorResult;
            }

            // 3.1 MD5对文件进行唯一验证
            var identifier = Request.Form["md5"];
            var fileMD5 = GetMD5HashFromFile(targetPath);
            if (!fileMD5.Equals(identifier))
            {
                // 删除本地错误文件
                System.IO.File.Delete(targetPath);
                var errMsg = "[" + clientFileName + "],文件MD5值不对等";
                var errorResult = JResult.Error(errMsg);
                // BzfLogger.Log(new SysLog(errMsg, "WebAPP.Api.Flow.RentDocController",
                //errorResult, "请求路径：" + Request.Url.AbsolutePath));               
                return errorResult;
            }
            return JResult.Success();
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
            var expireDate = DateTime.Now.AddMonths(-1); // 测试代码，超过1分钟 DateTime.Now.AddMinutes(-1);
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
        /// <para>自有业务是指，数字档案上传，则存在数字档案的表</para>
        /// <para>工作流的业务，则执行工作流的业务</para>
        /// </summary>
        /// <param name="targetPath"></param>
        /// <returns>返回文件对象</returns>
        private FileUploadInfo OwnBusiness(string targetPath)
        {
            FileUploadInfo fileResult = new FileUploadInfo();
            var pageType = Request["pagetype"];
            switch (pageType.ToLower())
            {
                case "flow":  // 工作流
                    fileResult = SaveFlowFileOrdinaryChunk(targetPath);
                    break;
                case "rent_doc_detail":   // 数字档案
                    fileResult = SaveFileOrdinaryChunk(targetPath);
                    break;
                case "rent_press_detail":   // 欠租追缴
                    fileResult = SavePressDetailFileOrdinaryChunk(targetPath);
                    break;
                default:    // 其他(附件)
                    fileResult = SaveAccessoryFileOrdinaryChunk(targetPath);
                    break;
            }
            return fileResult;
        }

        #endregion

        #region  文件上传后的自有业务

        /// <summary>
        /// 数字档案文件分片全部上传好后保存
        /// </summary>
        /// <param name="sourceFilePath">源文件路径</param>
        /// <returns></returns>
        public FileUploadInfo SaveFileOrdinaryChunk(string sourceFilePath)
        {
            try
            {
                var result = new FileUploadInfo();

                LockHelper.Open(sourceFilePath);
                var docService = new DocService();
                var newFileNamePath = GetDocFileName(docService, sourceFilePath);

                if (newFileNamePath == "FileExists")
                {  // 文件存在，则直接返回
                    LockHelper.Dispose(sourceFilePath);
                    return result;
                }

                string rentdoctbDetailId = Request["rentdoctbDetailId"];

                // 如果已经有详情了，则直接保存到数据库。
                if (!string.IsNullOrEmpty(rentdoctbDetailId))
                {
                    result = docService.SaveDocFile(sourceFilePath, newFileNamePath, rentdoctbDetailId);
                }
                else
                {
                    //新增的部分，存为临时文件,以.kyee 做为临时文件
                    FileInfo file = new FileInfo(sourceFilePath);
                    file.MoveTo(newFileNamePath);
                }
                LockHelper.Dispose(sourceFilePath);
                return result;
            }
            catch (Exception ex)
            {
                LockHelper.Dispose(sourceFilePath);
                throw ex;
            }
        }

        /// <summary>
        /// 获得数字档案文件路径+名称
        /// </summary>
        /// <returns></returns>
        public string GetDocFileName(DocService docService, string sourceFilePath)
        {
            string rentDoccode = Request["rentDoccode"];
            string rentDocProp = Request["rentDocProp"];
            string rentdocType = Request["rentdocType"];
            string rentdoctbDetailId = Request["rentdoctbDetailId"];

            var imagerName = (rentDoccode + "_" + rentDocProp + "_" + rentdocType);
            imagerName = imagerName.Replace(".", "0");  // 替换带有点的             

            var filePath = HMSUtil.GetDocPath() + DateTime.Today.ToString("yyyyMMdd");
            DicCreate(filePath);
            var fileIndex = Request["setIndex"];

            if (string.IsNullOrEmpty(fileIndex))
            {
                #region 生成最新的的文件下标

                if (rentdoctbDetailId.HasRealValue())
                {
                    //TODO:需要开启单线程
                    var currentMaxCount = docService.GetDetailMaxFileIndex(rentdoctbDetailId);
                    fileIndex = (currentMaxCount + 1).ToString().PadLeft(4, '0');
                }
                else
                {
                    // 循环取磁盘上最大的临时文件
                    DirectoryInfo dir = new DirectoryInfo(filePath);
                    FileInfo[] filearr = dir.GetFiles("*" + imagerName + ".kyee");
                    if (filearr.Any())
                    {
                        var indexList = new List<int>();
                        foreach (var item in filearr)
                        {
                            if (!item.Name.HasRealValue())
                            {
                                continue;
                            }
                            indexList.Add(item.Name.Split('.').FirstOrDefault().Split('_').LastOrDefault().ToInt());
                        }
                        fileIndex = (indexList.Max() + 1).ToString().PadLeft(4, '0');
                    }
                    else
                    {
                        fileIndex = "0001";
                    }
                }

                #endregion
            }

            var fileName = imagerName + "_" + fileIndex + Path.GetExtension(sourceFilePath);
            var newFileNamePath = filePath + "\\" + fileName;

            // 如果强制改名时，出现重复文件，则直接返回 
            if (fileIndex.HasRealValue())
            {
                if (System.IO.File.Exists(newFileNamePath))
                    return "FileExists";
            }

            // 如果是没有保存过的，则需要生成 .kyee 后缀的临时文件
            if (string.IsNullOrEmpty(rentdoctbDetailId))
            {
                newFileNamePath = newFileNamePath + "." + imagerName + ".kyee";
            }

            if (System.IO.File.Exists(newFileNamePath))
                System.IO.File.Delete(newFileNamePath);

            return newFileNamePath;
        }

        /// <summary>
        /// 工作流附件文件分片全部上传好后保存
        /// </summary>
        /// <param name="addFileName"></param>
        /// <param name="flowId"></param>
        /// <returns></returns>
        public FileUploadInfo SaveFlowFileOrdinaryChunk(string addFileName)
        {
            if (!addFileName.HasRealValue())
                throw new Exception("请选择文件");

            string fileName = Request["filename"];
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = Server.UrlDecode(fileName);
            }

            string tempDirName = Request["tempDirName"];

            bool isTemp = false;
            int sysMaxLength = HMSUtil.GetConfig(EnuInitCode.MaxAttFileSize.ToString()).ToInt(30);
            int MaxFileLength = 1024 * 1024 * sysMaxLength;
            FileInfo file = new FileInfo(addFileName);
            string dirName = tempDirName.HasRealValue() ? tempDirName : HMSUtil.GetFlowDirName();

            var userToken = AMSAuthentication.GetAuthToken();
            var service = new HMSServiceBase();
            var db = service.Init();
            decimal flowId = Request["flowId"].ToDecimal(0);
            var temp = db.Queryable<FlowTemporaryEntity>()
                .Where(x => x.CreateUser == userToken.UserId && x.FlowId == flowId)
                .First();
            if (temp != null && temp.UploadFolder.HasRealValue())
            {
                dirName = temp.UploadFolder;
                isTemp = true;
            }
            string direction = $@"{new FlowConfig().UploadRoot}\{dirName}\";

            long fileSize = 0;
            try
            {
                if (file.Length > MaxFileLength)
                {
                    file.Delete();
                    throw new Exception($"上传的文件的大小超过限制{sysMaxLength}M");
                }
                fileSize = file.Length;
                if (!Directory.Exists(direction))
                    FileHelper.CreateDirectory(direction);
                string destinationPath = Path.Combine(direction, fileName);
                if (System.IO.File.Exists(destinationPath))
                    System.IO.File.Delete(destinationPath);
                file.MoveTo(destinationPath);
            }
            catch (Exception ex)
            {
                file.Delete();
                WebLogger.Log($"FileExp:{ex.Message}");
                throw new Exception(ex.Message);
            }
            var url = new FlowCommonService().GetAttrUrl(fileName, dirName);
            var result = new FileUploadInfo
            {
                FileName = fileName,
                DirectorName = dirName,
                FileSize = fileSize,
                CreateUser = userToken.RealName,
                CreateTime = DateTime.Now,
                Url = url,
                IsTemp = isTemp,
            };
            return result;
        }

        /// <summary>
        /// 附件文件分片全部上传好后保存
        /// </summary>
        /// <param name="addFileName"></param>
        /// <param name="flowId"></param>
        /// <returns></returns>
        public FileUploadInfo SavePressDetailFileOrdinaryChunk(string addFileName)
        {
            if (!addFileName.HasRealValue())
                throw new Exception("请选择文件");

            string fileName = Request["filename"];
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = Server.UrlDecode(fileName);
            }
            var locationid = Request["locationid"];
            var location = Request["location"];
            var LocationType = Request["LocationType"];

            if (!LocationType.HasRealValue())
            {
                LocationType = location;
            }
            var basePath = HMSUtil.GetDocPath(LocationType);
            var filePath = basePath + DateTime.Today.ToString("yyyyMMdd");

            DicCreate(filePath);

            int sysMaxLength = HMSUtil.GetConfig(EnuInitCode.MaxAttFileSize.ToString()).ToInt(30);
            int MaxFileLength = 1024 * 1024 * sysMaxLength;
            FileInfo file = new FileInfo(addFileName);

            var userToken = AMSAuthentication.GetAuthToken();
            var service = new HMSServiceBase();
            var db = service.Init();
            long fileSize = 0;
            try
            {
                if (!fileName.HasRealValue())
                {
                    throw new Exception("文件不能为空");
                }

                if (file.Length > MaxFileLength)
                {
                    file.Delete();
                    throw new Exception($"上传的文件的大小超过限制{sysMaxLength}M");
                }

                var fileNameSplits = fileName.Split('.');

                fileSize = file.Length;
                var newFileName = fileNameSplits.FirstOrDefault() + "-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + Path.GetExtension(fileName);

                string destinationPath = Path.Combine(filePath, newFileName);
                file.MoveTo(destinationPath);

                //对指定格式才会对其进行缩略图的整合
                if (new Regex(@"\.(?:jpg|bmp|gif|png)$", RegexOptions.IgnoreCase).IsMatch(fileName.Trim()))
                {
                    var targetfilename = destinationPath.Insert(destinationPath.LastIndexOf("."), "-Thum");
                    if (System.IO.File.Exists(targetfilename))
                        System.IO.File.Delete(targetfilename);

                    ImageHelper.MakeThumbnail(destinationPath, targetfilename, 100, 100, "Cut", fileNameSplits.LastOrDefault());
                }

                var accessoryService = new AccessoryService();
                var accessory = accessoryService.GetBy(location, locationid, "签收证明", fileNameSplits.FirstOrDefault(), fileNameSplits.LastOrDefault());
                if (accessory == null || !accessory.AccessoryId.HasRealValue())
                {
                    accessory = new AccessoryEntity()
                    {
                        AccessoryLocation = location,
                        AccessoryLocationId = locationid,
                        AccessoryName = "签收证明",
                        CreateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        CreateUser = AMSAuthentication.GetAuthToken().UserId,
                        CreateUserName = AMSAuthentication.GetAuthToken().RealName,
                        OwnerId = "12345678"
                    };
                }
                else
                {
                    // 如果已经存在，是修改，则删除之前的文件
                    var deletePath = basePath + accessory.FileName;
                    if (System.IO.File.Exists(deletePath))
                    {
                        System.IO.File.Delete(deletePath);
                    }
                }

                // 修改和更新都需要的共用部分
                accessory.FileName = DateTime.Today.ToString("yyyyMMdd") + "/" + newFileName;
                accessory.FileSize = fileSize;
                accessory.LastestAccessDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                return accessoryService.SavePresstbDetailAccessory(accessory);
            }
            catch (Exception ex)
            {
                file.Delete();
                WebLogger.Log($"FileExp:{ex.Message}");
                //  return JResult.Error("上传未成功，请联系网站管理员");
                throw new Exception(ex.Message);
            }
        }

        /// <summary>
        /// 附件文件分片全部上传好后保存
        /// </summary>
        /// <param name="addFileName"></param>
        /// <param name="flowId"></param>
        /// <returns></returns>
        public FileUploadInfo SaveAccessoryFileOrdinaryChunk(string addFileName)
        {
            if (!addFileName.HasRealValue())
                throw new Exception("请选择文件");

            string fileName = Request["filename"];
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = Server.UrlDecode(fileName);
            }
            var locationid = Request["locationid"];
            var location = Request["location"];
            var LocationType = Request["LocationType"];

            if (!LocationType.HasRealValue())
            {
                LocationType = location;
            }
            var basePath = HMSUtil.GetDocPath(LocationType);
            var filePath = basePath + DateTime.Today.ToString("yyyyMMdd");
            DicCreate(filePath);

            int sysMaxLength = HMSUtil.GetConfig(EnuInitCode.MaxAttFileSize.ToString()).ToInt(30);
            int MaxFileLength = 1024 * 1024 * sysMaxLength;
            FileInfo file = new FileInfo(addFileName);

            var userToken = AMSAuthentication.GetAuthToken();
            var service = new HMSServiceBase();
            var db = service.Init();
            long fileSize = 0;
            try
            {
                if (file.Length > MaxFileLength)
                {
                    file.Delete();
                    throw new Exception($"上传的文件的大小超过限制{sysMaxLength}M");
                }

                fileSize = file.Length;
                var newFileName = fileName.Split('.').FirstOrDefault() + "-" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(fileName);

                string destinationPath = Path.Combine(filePath, newFileName);
                if (System.IO.File.Exists(destinationPath))
                    System.IO.File.Delete(destinationPath);
                file.MoveTo(destinationPath);

                var accessoryService = new AccessoryService();
                var accessory = accessoryService.GetBy(location, locationid, fileName);
                if (accessory == null || !accessory.AccessoryId.HasRealValue())
                {
                    accessory = new AccessoryEntity()
                    {
                        AccessoryLocation = location,
                        AccessoryLocationId = locationid,
                        AccessoryName = fileName,
                        CreateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        CreateUser = AMSAuthentication.GetAuthToken().UserId,
                        CreateUserName = AMSAuthentication.GetAuthToken().RealName,
                        CompanyId = AMSAuthentication.GetAuthToken().CompanyId,
                        OwnerId = "12345678"
                    };
                }
                else
                {
                    // 如果已经存在，是修改，则删除之前的文件
                    var deletePath = basePath + accessory.FileName;
                    if (System.IO.File.Exists(deletePath))
                    {
                        System.IO.File.Delete(deletePath);
                    }
                }

                // 修改和更新都需要的共用部分
                accessory.FileName = DateTime.Today.ToString("yyyyMMdd") + "/" + newFileName;
                accessory.FileSize = fileSize;
                accessory.LastestAccessDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                return accessoryService.SaveAccessory(accessory);
            }
            catch (Exception ex)
            {
                file.Delete();
                WebLogger.Log($"FileExp:{ex.Message}");
                throw new Exception(ex.Message);
            }
        }

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

        #endregion
    }

    public class JResult
    {
        internal static object Error(string errMsg)
        {
            throw new NotImplementedException();
        }

        internal static object Success()
        {
            throw new NotImplementedException();
        }
    }
    public class CommonResult
    {
        public object Data { get; set; }
        public string Message { get; set; }

        public bool Result { get; set; }
    }
}
