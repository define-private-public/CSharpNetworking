// Filename:  client.c
// Author:    Benjamin N. Summerton <define-private-public>
// License:   Unlicense (https://unlicense.org/)
//
// Adapted From:
//   https://en.wikibooks.org/wiki/C_Programming/Networking_in_UNIX

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <unistd.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <arpa/inet.h>
#include <netinet/in.h>


const int MAX_RECV_LEN = 255;
const int PORT_NUM = 6000;


// Main method
int main(int argc, char *argv[]) {
    char buffer[MAX_RECV_LEN + 1];
    int len, clientSocket;
    struct sockaddr_in serv;

    // Create a TCP/IP socket
    clientSocket = socket(AF_INET, SOCK_STREAM, 0);
    memset(&serv, 0, sizeof(serv));
    serv.sin_family = AF_INET;
    serv.sin_addr.s_addr = htonl(INADDR_LOOPBACK);  // 127.0.0.1 (a.k.a. localhost)
    serv.sin_port = htons(PORT_NUM);
    
    // Connect to the server
    printf("Connecting to the server...\n");
    connect(clientSocket, (struct sockaddr *)&serv, sizeof(struct sockaddr));

    // Get a message (blocks)
    len = recv(clientSocket, buffer, MAX_RECV_LEN, 0);
    buffer[len] = '\0';     // Null terminate the string
    printf("Got a message from the server [%i bytes]:\n%s", len, buffer);

    // cleanup
    close(clientSocket);
    return 0;
}

