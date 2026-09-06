window.swlwDownload = function (fileName, contentType, bytes) {
  const array = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  const blob = new Blob([array], { type: contentType || "application/octet-stream" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
};
