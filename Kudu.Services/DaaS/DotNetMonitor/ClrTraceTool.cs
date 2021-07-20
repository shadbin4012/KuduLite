﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Kudu.Services.DaaS
{
    class ClrTraceTool : DotNetMonitorToolBase
    {
        internal override async Task<DiagnosticToolResponse> InvokeDotNetMonitorAsync(string path, string temporaryFilePath, string fileExtension, string instanceId)
        {
            var toolResponse = new DiagnosticToolResponse();
            if (string.IsNullOrWhiteSpace(dotnetMonitorAddress))
            {
                return toolResponse;
            }

            try
            {
                var tasks = new Dictionary<DotNetMonitorProcessResponse, Task<HttpResponseMessage>>();
                foreach (var p in await GetDotNetProcessesAsync())
                {
                    var process = await GetDotNetProcessAsync(p.pid);
                    tasks.Add(process, _dotnetMonitorClient.GetAsync(
                        path.Replace("{processId}", p.pid.ToString()),
                        HttpCompletionOption.ResponseHeadersRead));
                }

                foreach (var task in tasks)
                {
                    var process = task.Key;
                    var resp = await task.Value;

                    if (resp.IsSuccessStatusCode)
                    {
                        string fileName = resp.Content.Headers.ContentDisposition.FileName;
                        if (string.IsNullOrWhiteSpace(fileName))
                        {
                            fileName = DateTime.UtcNow.Ticks.ToString() + fileExtension;
                        }

                        fileName = $"{instanceId}_{process.name}_{process.pid}_{fileName}";
                        fileName = Path.Combine(temporaryFilePath, fileName);
                        using (var stream = await resp.Content.ReadAsStreamAsync())
                        {
                            using (var fileStream = new FileStream(fileName, FileMode.CreateNew))
                            {
                                await stream.CopyToAsync(fileStream);
                            }
                        }

                        toolResponse.Logs.Add(new LogFile()
                        {
                            FullPath = fileName,
                            ProcessName = process.name,
                            ProcessId = process.pid
                        });
                    }
                    else
                    {
                        var error = await resp.Content.ReadAsStringAsync();
                        toolResponse.Errors.Add(error);
                    }
                }
            }
            catch (Exception ex)
            {
                toolResponse.Errors.Add(ex.Message);
            }

            return toolResponse;
        }
        public override async Task<DiagnosticToolResponse> InvokeAsync(string toolParams, string temporaryFilePath, string instanceId)
        {
            ClrTraceParams clrTraceParams = new ClrTraceParams(toolParams);
            string path = $"{dotnetMonitorAddress}/trace/{{processId}}?durationSeconds={clrTraceParams.DurationSeconds}&profile={clrTraceParams.TraceProfile}";
            var response = await InvokeDotNetMonitorAsync(path, temporaryFilePath, fileExtension: ".nettrace", instanceId);
            return response;
        }
    }
}
