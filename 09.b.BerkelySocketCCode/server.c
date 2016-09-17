// Filename:  server.c
// Author:    Benjamin N. Summerton <define-private-public>
// License:   Unlicense (https://unlicense.org/)
//
// Adapted From:
//   https://en.wikibooks.org/wiki/C_Programming/Networking_in_UNIX

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <signal.h>

#include <unistd.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <arpa/inet.h>
#include <netinet/in.h>


const int PORT_NUM = 6000;
int running = 0;
int serverSocket = 0;               // Socket to listen for incoming connections


// SIGINT handler
void sigintHandler(int sig) {
    if (sig == SIGINT) {
        printf("Received SIGINT, shutting down server.\n");

        // Cleanup
        running = 0;
        if (serverSocket)
            close(serverSocket);

        // end program
        exit(0);
    }
}
        

// Main Method
int main(int argc, char *argv[]) {
    char *msg = "Hello, Client!\n";

    struct sockaddr_in dest;    // socket info about remote machine
    struct sockaddr_in serv;    // socket info about us
    int clientSocket;           // Socket FD for the remote client
    socklen_t socksize = sizeof(struct sockaddr_in);

    // Init and create the socket
    memset(&serv, 0, sizeof(serv));             // Zero out struct before filling
    serv.sin_family = AF_INET;                  // Mark as TCP/IP
    serv.sin_addr.s_addr = htonl(INADDR_ANY);   // Put it on any interface
    serv.sin_port = htons(PORT_NUM);            // Set server port number

    serverSocket = socket(AF_INET, SOCK_STREAM, 0);

    // Bind serv information to the socket
    bind(serverSocket, (struct sockaddr *)&serv, sizeof(struct sockaddr));

    // Start listening for new connections (queue of 5 max)
    listen(serverSocket, 5);

    // Setup SIGINT handler
    if (signal(SIGINT, sigintHandler) != SIG_ERR) {
        running = 1;
        printf("Running the TCP server.\n");
    }

    // Main loop
    while (running) {
        // Wait for a new client (blocks)
        clientSocket = accept(serverSocket, (struct sockaddr *)&dest, &socksize);

        // print some info about the remote client 
        printf("Incoming connection from %s, replying.\n", inet_ntoa(dest.sin_addr));

        // Send a reply (blocks)
        send(clientSocket, msg, strlen(msg), 0);

        // Close the connection
        close(clientSocket);
    }

    return 0;
}

