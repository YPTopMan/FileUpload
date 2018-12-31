// 文件块信息集合
var kyeeFileBlock = [];

var uploadFileCount = 0;  // 文件上传数量

// 上传
function uploadFile() {
    // 触发上传按钮
    $("#filePicker").find("label").click();
    setUploadFilePath();
}

// 设置上传的地址与参数
function setUploadFilePath() {
    var getMaxChunk = '/FileUpload/GetMaxChunk?pagetype=' + uploadParm.PageType
    var mergeFiles = '/FileUpload/MergeFiles?pagetype=' + uploadParm.PageType;
    FILE_PATH.GetMaxChunk = getMaxChunk + uploadParm.Parm;
    FILE_PATH.MergeFiles = mergeFiles + uploadParm.Parm;
    uploader.options.server = FILE_PATH.server + uploadParm.Parm;
}

var FILE_PATH = {
    //swf所在路径
    swf: '~/Areas/HMS/Content/webuploader/Uploader.swf',
    //处理文件上传的地址
    server: '/FileUpload/ChunkUpload?pagetype=' + uploadParm.PageType,
    //获取已上传文件的块数量
    GetMaxChunk: '', //'/FileUpload/GetMaxChunk?pagetype='+pagetype,
    //进行文件合并的地址
    MergeFiles: ''//'/FileUpload/MergeFiles?pagetype='+pagetype,
};


//注册断点续传，必选在 WebUploader.create 注册之前
WebUploader.Uploader.register({
    'add-file': 'addFiles',
    'before-send': 'beforeSend',
    'after-send-file': 'afterSendFile'
}, {
    addFiles: function (files) {
        // 遍历files中的文件, 过滤掉不满足规则的。       
    },
    beforeSend: function (block) {
        // 开始的块数（片数）
        var startChunk = 0;
        var currentFile = block.file;
        for (var i = 0; i < kyeeFileBlock.length; i++) {
            // 服务器的切片文件信息
            var serverBlockFile = kyeeFileBlock[i];
            if (serverBlockFile.md5 == currentFile.md5) {
                startChunk = parseInt(serverBlockFile.chunk);
                break;
            }
        }
        var task = new $.Deferred();

        // 需要跳过的块（片）
        if (startChunk > 0 && block.chunk <= startChunk) {
            task.reject();
        } else {
            task.resolve();
        }

        return $.when(task);
    },
    afterSendFile: function (file) {
        uploadFileCount++;
    }
});

var uploader = WebUploader.create({
    // swf文件路径
    swf: FILE_PATH.swf,
    // 文件接收服务端。
    server: FILE_PATH.server,
    // 选择文件的按钮。可选。
    // 内部根据当前运行是创建，可能是input元素，也可能是flash.
    pick: {
        id: '#filePicker',
        label: '点击选择文件'
    },
    chunked: true, //分片处理大文件
    chunkSize: 0.3 * 1024 * 1024,
    duplicate: true,   // 可重复上传同一文件
    threads: 1, //上传并发数
    fileNumLimit: 300,
    compress: false, //图片在上传前不进行压缩
    fileSizeLimit: 1024 * 1024 * 1024,    // 1024 M
    fileSingleSizeLimit: 1024 * 1024 * 1024    // 1024 M
});

var selectionsort = 1;
var progress;
// 当文件被加入队列之前触发
uploader.on('beforeFileQueued', function (file, file2) {
    file.selectionsort = selectionsort;
    selectionsort = selectionsort + 1;
});

// 当文件被加入队列以后触发。
uploader.on('fileQueued', function (file) {
    addFile(file);
    uploader.md5File(file).progress(function (percentage) {
    }).then(function (val) {
        //  console.log("md5", file.name + "," + val + ",排序号:" + file.selectionsort);
        if (!val) {
            console.log("%c 空MD5值,文件" + file.name, 'color: red');
        }
        file.md5 = val;
        $.ajax({
            url: FILE_PATH.GetMaxChunk,
            async: false,
            type: "get",
            data: { md5: file.md5, ext: file.ext },
            success: function (response) {
                if (response.Code == "0") {
                    if (response.Data && parseInt(response.Data) > 1) {
                        //片信息
                        kyeeFileBlock.push({ id: file.id, md5: val, size: file.size, ext: file.ext, chunk: response.Data });
                    }

                    uploader.upload(file);  // 指定文件上传    
                } else {
                    console.log("%c 检测切片," + file.name + "," + response.Message, 'color: red');
                }
            }
        });
    });
});

// 当一批文件添加进队列以后触发。
uploader.on('filesQueued', function () {
    initProgress();
    progress = $.ligerDialog.open({ title: "上传进度", target: $("#fileProgress"), width: 500 });

    // 重置排序
    selectionsort = 1;
});

// 当某个文件的分块在发送前触发
uploader.on('uploadBeforeSend', function (object, data, headers) {
    if (!object.file.md5) {
        console.log("%c 异常,文件[" + data.name + ",总计" + data.chunks + "片,第" + data.chunk + "片,无MD5值]", 'color: red');
    }
    data.md5 = object.file.md5;
});

// 当某个文件上传到服务端响应后，会派送此事件来询问服务端响应是否有效
uploader.on('uploadAccept', function (object, response) {
    if (response.Code == "-1") {
        console.log("%c 服务器返回," + object.file.name + ",总计" + object.chunks + "片,第" + object.chunk + "片，" + response.Message, 'color: red');
    }
});

// 文件上传成功,合并文件。
uploader.on('uploadSuccess', function (file, response) {
    if (response && response.Code >= 0) {
        var dataObj = response.Message;
        var md5 = file.md5;
        if (dataObj == 'chunked') {
            $.ajax({
                url: FILE_PATH.MergeFiles,
                type: "post", data: { md5: md5, filename: file.name, fileTotalSize: file.size },
                async: false,
                success: function (data) {
                    if (!data.Result) {
                        $("#fp_" + file.id).find("span").html("").html(data.Message);
                        alert('%c 文件合并失败！' + file.name + "," + data.Message, 'color: red');
                    } else {
                        // console.info('上传文件完成并合并成功，触发回调事件');
                        if (window.UploadSuccessCallback) {
                            window.UploadSuccessCallback(file, md5, data);
                        }
                    }
                }
            });
        }
        else {
            //  console.info('上传文件完成，不需要合并，触发回调事件');
            if (window.UploadSuccessCallback) {
                window.UploadSuccessCallback(file, md5);
            }
        }
    }
});

//当文件上传出错时触发。
uploader.on('uploaderror', function (file, reason) {

    alert("上传失败" + reason);
});

//
uploader.onError = function (code) {
    alert('上传失败，错误： ' + code);
};

//显示进度百分比
uploader.onUploadProgress = function (file, percentage) {
    var bf = percentage * 100 + '%';
    $("#pg_" + file.id).val(percentage * 100);
};

//当所有文件上传结束时触发
uploader.onUploadFinished = function () {
    var fileCount = uploader.getFiles().length;
    console.log(fileCount, uploadFileCount);
    //全部上传完成后
    if (parseInt(fileCount) == uploadFileCount) {

        if (window.pagingData) {
            // 累计上传个数，计算最后一页的页码
            if (pagingData.RowCount) {
                pagingData.RowCount += fileCount;
            } else {
                pagingData.RowCount = fileCount;
            }
        }    

        uploader.reset();  // 重置队列
        if (window.UploadFinishedCallback) {
            window.UploadFinishedCallback();
        }
        setTimeout(function () { progress.close(); $("#fileProgress").remove(); }, 500);
        uploadFileCount = 0;
    }
};


/*--------自有业务添加------*/
// 当有文件添加进来时执行，负责view的创建
function addFile(file) {
    var fileSize = parseInt(file.size);
    var mbSize = (fileSize / 1024 / 1024).toFixed(2);
    var $ps = $('<div id="fp_' + file.id + '">' +
     ' <p>' + file.name + '(' + mbSize + 'MB)<span style="color:red;"></span></p>' +
     ' <progress max="100" style="width:475px;" value="0" id="pg_' + file.id + '"></progress> ' +
     '</div>');
    initProgress();
    var $div = $("#fileProgress");
    if ($div.children('div').size() == 0) {
        $div.append($ps);
    } else {
        $div.children('div:last').after($ps);
    }
}

function initProgress() {
    if ($("#fileProgress").length == 0) {
        $("body").append("<div id='fileProgress'></div>");
    }
}