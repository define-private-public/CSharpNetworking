# Filename:  Makefile
# Author:    Benjamin N. Summerton <define-private-public>
# License:   Unlicense (https://unlicense.org/)

# Change the value you're preferred c compiler (e.g. `clang` or `gcc`)
CC=clang

all: server client

server: server.c
	$(CC) -o server server.c

client: client.c
	$(CC) -o client client.c

clean:
	rm server
	rm client

