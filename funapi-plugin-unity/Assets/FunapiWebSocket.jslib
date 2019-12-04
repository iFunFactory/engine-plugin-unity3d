var LibraryWebSockets = {
$webSocketInstances: [],

SocketJSCreate: function(url)
{
    var str = Pointer_stringify(url);
    var socket = {
        socket: new WebSocket(str),
        buffer: new Uint8Array(0),
        close_code: 0,
        close_reason: null,
        error: null,
        messages: []
    }

    socket.socket.binaryType = 'arraybuffer';

    socket.socket.onmessage = function (e) {
        // Todo: handle other data types?
        if (e.data instanceof Blob)
        {
            var reader = new FileReader();
            reader.addEventListener("loadend", function() {
                var array = new Uint8Array(reader.result);
                socket.messages.push(array);
            });
            reader.readAsArrayBuffer(e.data);
        }
        else if (e.data instanceof ArrayBuffer)
        {
            var array = new Uint8Array(e.data);
            socket.messages.push(array);
        }
    };

    socket.socket.onclose = function (e) {
        socket.close_code = e.code;
        if (e.reason != null && e.reason.length > 0)
            socket.close_reason = e.reason;
    }

    socket.socket.onerror = function (e) {
        socket.error = e.message;
    }

    var instance = webSocketInstances.push(socket) - 1;
    return instance;
},

SocketJSState: function (socketInstance)
{
    var socket = webSocketInstances[socketInstance];
    return socket.socket.readyState;
},

SocketJSError: function (socketInstance)
{
    var socket = webSocketInstances[socketInstance];
    return socket.error;
},

SocketJSCloseReason: function (socketInstance)
{
    var socket = webSocketInstances[socketInstance];
    return socket.close_reason;
},

SocketJSCloseCode: function (socketInstance)
{
    var socket = webSocketInstances[socketInstance];
    return socket.close_code;
},

SocketJSSend: function (socketInstance, ptr, length)
{
    var socket = webSocketInstances[socketInstance];
    socket.socket.send (HEAPU8.buffer.slice(ptr, ptr+length));
},

SocketJSRecvLength: function(socketInstance)
{
    var socket = webSocketInstances[socketInstance];
    if (socket.messages.length == 0)
        return 0;
    return socket.messages[0].length;
},

SocketJSRecv: function (socketInstance, ptr, length)
{
    var socket = webSocketInstances[socketInstance];
    if (socket.messages.length == 0)
        return 0;
    if (socket.messages[0].length > length)
        return 0;
    HEAPU8.set(socket.messages[0], ptr);
    socket.messages = socket.messages.slice(1);
},

SocketJSClose: function (socketInstance)
{
    var socket = webSocketInstances[socketInstance];
    socket.socket.close();
}
};

autoAddDeps(LibraryWebSockets, '$webSocketInstances');
mergeInto(LibraryManager.library, LibraryWebSockets);
