"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
const json_rpc_2_0_1 = require("../node_modules/json-rpc-2.0");
const webSocket = new WebSocket("ws://localhost:44343/jrpc");
//#region декларация
const serverAndClient = new json_rpc_2_0_1.JSONRPCServerAndClient(new json_rpc_2_0_1.JSONRPCServer(), new json_rpc_2_0_1.JSONRPCClient(request => {
    try {
        webSocket.send(JSON.stringify(request));
        return Promise.resolve();
    }
    catch (error) {
        return Promise.reject(error);
    }
}));
// On close, make sure to reject all the pending requests to prevent hanging.
webSocket.onclose = (event) => {
    serverAndClient.rejectAllPendingRequests(`Connection is closed (${event.reason}).`);
};
webSocket.onmessage = (event) => {
    serverAndClient.receiveAndSend(JSON.parse(event.data.toString()));
};
//#endregion
webSocket.onopen = async () => {
    try {
        var result = await serverAndClient.request("Test/Add", [1, 2]);
        console.log(`1 + 2 = ${result}`);
    }
    catch (e) {
        console.error(e);
    }
};
serverAndClient.addMethod("Test/Echo", (args) => {
    const [text, tel] = args;
    return text + ' ' + tel;
});
//# sourceMappingURL=vrpc.js.map