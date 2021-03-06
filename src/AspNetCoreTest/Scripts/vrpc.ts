import { JSONRPCClient, JSONRPCServerAndClient, JSONRPCServer } from "json-rpc-2.0";

function App() {
    const webSocket = new WebSocket("ws://localhost:1234");

    //#region декларация
    const serverAndClient = new JSONRPCServerAndClient(
        new JSONRPCServer(),
        new JSONRPCClient(request => {
            try {
                webSocket.send(JSON.stringify(request))
                return Promise.resolve();
            } catch (error) {
                return Promise.reject(error);
            }
        })
    );

    // On close, make sure to reject all the pending requests to prevent hanging.
    webSocket.onclose = (event) => {
        serverAndClient.rejectAllPendingRequests(`Connection is closed (${event.reason}).`);
    }
    webSocket.onmessage = (event) => {
        serverAndClient.receiveAndSend(JSON.parse(event.data.toString()));
    }
    //#endregion

    webSocket.onopen = async () => {
        try {
            var result = await serverAndClient.request("Test/Add", [1, 2]);
            console.log(`1 + 2 = ${result}`);
        }
        catch (e) {
            console.error(e);
        }
    }

    serverAndClient.addMethod("Test/Echo", (args: any) => {
        const [text, tel]: [string, number] = args;
        return text + ' ' + tel;
    });
}
