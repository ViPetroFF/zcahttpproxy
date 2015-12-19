// LibVLCHook.cpp : Defines the exported functions for the DLL application.
//

#include "stdafx.h"
#include "detours.h"

extern "C" __declspec( dllexport ) HRESULT WINAPI CAServerAddressInit(LPCSTR lpszHost, WORD wPort);
extern "C" __declspec( dllexport ) DWORD WINAPI GetBestMacAddress(PUCHAR uchPhysAddr, DWORD dwSize);

HRESULT MACAddressSearch(LPCSTR lpszIP, PUCHAR uchPhysAddr, int iSize);

#include "verify.cpp"

static BOOL bDoReplacePhysAddress = FALSE;
static BOOL bIsPhysAddressFilled = FALSE;
static BOOL bIsBestPhysAddressFilled = FALSE;
static UCHAR uchPhysAddress[] = {0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
static UCHAR uchBestPhysAddress[] = {0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
static DWORD dwBestPhysAddrLen = 0;
static WORD wProxyPort = 0;

static DWORD (WINAPI * TrueGetIfEntry)(PMIB_IFROW pIfRow) = NULL;
static HINTERNET (WINAPI * TrueInternetConnectA)(
									HINTERNET hInternet,
									LPCSTR lpszServerName,
									INTERNET_PORT nServerPort,
									LPCSTR lpszUsername,
									LPCSTR lpszPassword,
									DWORD dwService,
									DWORD dwFlags,
									DWORD_PTR dwContext) = NULL;


DWORD WINAPI MacGetIfEntry(PMIB_IFROW pIfRow)
{
	//printf("LibVLCHook.dll: DWORD WINAPI MacGetIfEntry(PMIB_IFROW pIfRow).\n");
    DWORD dwRet = TrueGetIfEntry(pIfRow);

	if(NO_ERROR == dwRet)
	{
		if((bDoReplacePhysAddress || 0 == pIfRow->dwPhysAddrLen) && TRUE == bIsPhysAddressFilled)
		{
			memcpy(pIfRow->bPhysAddr, uchPhysAddress, sizeof(uchPhysAddress));
			pIfRow->dwPhysAddrLen = sizeof(uchPhysAddress);
		}

		if(pIfRow->dwPhysAddrLen > 0)
		{
			DWORD dwPhysAddrLen = pIfRow->dwPhysAddrLen;
			if(dwPhysAddrLen > sizeof(uchBestPhysAddress))
			{
				dwPhysAddrLen = sizeof(uchBestPhysAddress);
			}
			memcpy(uchBestPhysAddress, pIfRow->bPhysAddr, dwPhysAddrLen);
			dwBestPhysAddrLen = dwPhysAddrLen;
			bIsBestPhysAddressFilled = TRUE;
		}
	}

	return dwRet;
}

HINTERNET WINAPI PortInternetConnectA(
						HINTERNET hInternet,
						LPCSTR lpszServerName,
						INTERNET_PORT nServerPort,
						LPCSTR lpszUsername,
						LPCSTR lpszPassword,
						DWORD dwService,
						DWORD dwFlags,
						DWORD_PTR dwContext)
{
	//printf("LibVLCHook.dll: HINTERNET WINAPI PortInternetConnectA(%s, %d).\n", lpszServerName, nServerPort);

	return TrueInternetConnectA(
						hInternet,
						lpszServerName,
						(wProxyPort > 0)?wProxyPort:nServerPort,
						lpszUsername,
						lpszPassword,
						dwService,
						dwFlags,
						dwContext
		);
}

static inline bool isValidIpAddress(const char* ipAddress)
{
#if 0 // Vista and upper
    struct sockaddr_in sa;
    int result = inet_pton(AF_INET, ipAddress, &(sa.sin_addr));

    return result != 0;
#endif // 0

	bool bIsValid = false;
	unsigned b1, b2, b3, b4;
	unsigned char c;

	if (4 == sscanf(ipAddress, "%3u.%3u.%3u.%3u%c", &b1, &b2, &b3, &b4, &c))
	{

		if ((b1 | b2 | b3 | b4) <= 255)
		{
			if (strlen(ipAddress) == strspn(ipAddress, "0123456789."))
			{
				bIsValid = true;
			}
		}		
	}

	return bIsValid;
}

HRESULT WINAPI CAServerAddressInit(LPCSTR lpszIP, WORD wPort)
{
	//printf("LibVLCHook.dll: HRESULT WINAPI CAServerAddressInit(%s, %d).\n", (NULL == lpszIP)?"null":lpszIP, wPort);
	//fprintf(stderr, "LibVLCHook.dll: HRESULT WINAPI CAServerAddressInit(%s, %d).\n", (NULL == lpszIP)?"null":lpszIP, wPort);

	HRESULT hr = ERROR_SUCCESS;
	UCHAR uchPhysAddr[]={0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
	if(!lpszIP || !*lpszIP || isValidIpAddress(lpszIP))
	{
		if(ERROR_SUCCESS == (hr = MACAddressSearch(lpszIP, uchPhysAddr, sizeof(uchPhysAddr))))
		{
			memcpy(uchPhysAddress, uchPhysAddr, sizeof(uchPhysAddr));
			bIsPhysAddressFilled = TRUE;
	#if 0
			printf("!!!!!! Physical address: ");
			for (int i = 0; i < (int) sizeof(uchPhysAddress);
				 i++) {
				if (i == (sizeof(uchPhysAddress) - 1))
					printf("%.2X\n",
						   (int) uchPhysAddress[i]);
				else
					printf("%.2X-",
						   (int) uchPhysAddress[i]);
			}
	#endif // 0
		}
	}
	else
	{
		WORD wPhysAddr[]={0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
		if( sizeof(uchPhysAddr) == sscanf(lpszIP, "%02hx:%02hx:%02hx:%02hx:%02hx:%02hx",
			wPhysAddr, wPhysAddr+1, wPhysAddr+2, wPhysAddr+3, wPhysAddr+4, wPhysAddr+5))
		{
			for(int i=0; i<sizeof(uchPhysAddress); i++)
			{
				uchPhysAddress[i] = (UCHAR)wPhysAddr[i];
			}
			//memcpy(uchPhysAddress, uchPhysAddr, sizeof(uchPhysAddr));
			bIsPhysAddressFilled = TRUE;
			bDoReplacePhysAddress = TRUE;
		}
		else
		{
			hr = E_INVALIDARG;
		}
	}

	if(SUCCEEDED(hr))
	{
		wProxyPort = wPort;
	}

	//fprintf(stderr, "LibVLCHook.dll: HRESULT WINAPI CAServerAddressInit = %d.\n", hr);

	return hr;
}

DWORD WINAPI GetBestMacAddress(PUCHAR uchPhysAddr, DWORD dwSize)
{
	DWORD dwPhysAddrLen = 0;

	if(bIsBestPhysAddressFilled)
	{
		dwPhysAddrLen = dwBestPhysAddrLen;
		if(dwSize < dwPhysAddrLen)
		{
			dwPhysAddrLen = dwSize;
		}
		memcpy(uchPhysAddr, uchBestPhysAddress, dwPhysAddrLen);
	}

	return dwPhysAddrLen;
}

BOOL WINAPI DllMain(HINSTANCE hinst, DWORD dwReason, LPVOID reserved)
{
    LONG error;
    (void)hinst;
    (void)reserved;

    if (DetourIsHelperProcess())
	{
        return TRUE;
    }

    if (dwReason == DLL_PROCESS_ATTACH)
	{
        DetourRestoreAfterWith();

		TrueGetIfEntry = (DWORD (WINAPI *)(PMIB_IFROW pIfRow))DetourFindFunction("Iphlpapi.dll", "GetIfEntry");
		TrueInternetConnectA = ( HINTERNET (WINAPI *)(
									HINTERNET hInternet,
									LPCSTR lpszServerName,
									INTERNET_PORT nServerPort,
									LPCSTR lpszUsername,
									LPCSTR lpszPassword,
									DWORD dwService,
									DWORD dwFlags,
									DWORD_PTR dwContext))DetourFindFunction("Wininet.dll", "InternetConnectA");

        Verify("GetIfEntry", (PVOID)TrueGetIfEntry);
		Verify("InternetConnectA", (PVOID)TrueInternetConnectA);
        printf("\n");

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)TrueGetIfEntry, MacGetIfEntry);
		DetourAttach(&(PVOID&)TrueInternetConnectA, PortInternetConnectA);
        error = DetourTransactionCommit();

        if (error == NO_ERROR)
		{
			;
            //printf("LibVLCHook.dll: Detoured GetIfEntry(), InternetConnectA().\n");
        }
        else
		{
            printf("LibVLCHook.dll: Error detouring GetIfEntry(), InternetConnectA(): %d\n", error);
        }
    }
    else if (dwReason == DLL_PROCESS_DETACH)
	{
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourDetach(&(PVOID&)TrueGetIfEntry, MacGetIfEntry);
		DetourDetach(&(PVOID&)TrueInternetConnectA, PortInternetConnectA);
        error = DetourTransactionCommit();
        //printf("LibVLCHook.dll: Removed GetIfEntry(), InternetConnectA() detour (%d).\n", error);
        fflush(stdout);
    }

    return TRUE;
}


#define WORKING_BUFFER_SIZE 15000
#define MAX_TRIES 3

#define MALLOC(x) HeapAlloc(GetProcessHeap(), 0, (x))
#define FREE(x) HeapFree(GetProcessHeap(), 0, (x))

HRESULT MACAddressSearch(LPCSTR lpszIP, PUCHAR uchPhysAddr, int iSize)
{
	int retVal;

#if 0
	WORD wVersionRequested;
	WSADATA wsaData;

	wVersionRequested = MAKEWORD( 2, 2 );
	 
	if( 0 != (retVal = WSAStartup( wVersionRequested, &wsaData )))
	{
		// Tell the user that we could not find a usable
		// WinSock DLL.
		return retVal;
	}
#endif // 0
    // Declare and initialize variables

	struct addrinfo aiHints={0};
	struct sockaddr_in saAdapter={0};
	struct addrinfo iAdapterIp={0};
	struct addrinfo *aiList = &iAdapterIp;
	//--------------------------------
	// Setup the hints address info structure
	// which is passed to the getaddrinfo() function
	aiHints.ai_family = AF_INET;
	aiHints.ai_socktype = SOCK_STREAM;
	aiHints.ai_protocol = IPPROTO_TCP;

	if(lpszIP && *lpszIP)
	{
		// Set up the sockaddr structure
		iAdapterIp.ai_family = AF_INET;
		iAdapterIp.ai_socktype = SOCK_STREAM;
		iAdapterIp.ai_protocol = IPPROTO_TCP;

		saAdapter.sin_family = AF_INET;
		saAdapter.sin_addr.s_addr = inet_addr(lpszIP);
		iAdapterIp.ai_addr = (struct sockaddr *)&saAdapter;
		iAdapterIp.ai_addrlen = sizeof(saAdapter);
	}
	else
	{
		//--------------------------------
		// Call getaddrinfo(). If the call succeeds,
		// the aiList variable will hold a linked list
		// of addrinfo structures containing response
		// information about the host
		if ((retVal = getaddrinfo("", NULL, &aiHints, &aiList)) != 0)
		{
			printf("getaddrinfo() failed, error %s\n", gai_strerrorA(retVal));

			return retVal;
		}
	}

	for (struct addrinfo * lst = aiList; lst != NULL;)
	{
		printf("Host address: %s\n", inet_ntoa((struct in_addr)((struct sockaddr_in *)lst->ai_addr)->sin_addr));
		lst = lst->ai_next;
	}

    DWORD dwSize = 0;
    DWORD dwRetVal = ERROR_SUCCESS;

    // Set the flags to pass to GetAdaptersAddresses
    ULONG flags = GAA_FLAG_INCLUDE_PREFIX;

    // default to unspecified address family (both)
    ULONG family = AF_INET;

    LPVOID lpMsgBuf = NULL;

    PIP_ADAPTER_ADDRESSES pAddresses = NULL;
    ULONG outBufLen = 0;
    ULONG Iterations = 0;

    PIP_ADAPTER_ADDRESSES pCurrAddresses = NULL;
    PIP_ADAPTER_UNICAST_ADDRESS pUnicast = NULL;
    PIP_ADAPTER_ANYCAST_ADDRESS pAnycast = NULL;
    PIP_ADAPTER_MULTICAST_ADDRESS pMulticast = NULL;
    IP_ADAPTER_DNS_SERVER_ADDRESS *pDnServer = NULL;
    IP_ADAPTER_PREFIX *pPrefix = NULL;

    family = AF_INET;

    //printf("Calling GetAdaptersAddresses function with family = ");
    //if (family == AF_INET)
	//{
        //printf("AF_INET\n");
	//}
    //if (family == AF_INET6)
	//{
        //printf("AF_INET6\n");
	//}
    //if (family == AF_UNSPEC)
	//{
        //printf("AF_UNSPEC\n\n");
	//}

    // Allocate a 15 KB buffer to start with.
    outBufLen = WORKING_BUFFER_SIZE;

    do
	{
        pAddresses = (IP_ADAPTER_ADDRESSES *) MALLOC(outBufLen);
        if (pAddresses == NULL)
		{
            printf
                ("Memory allocation failed for IP_ADAPTER_ADDRESSES struct\n");
            return ERROR_NOT_ENOUGH_MEMORY;
        }

        dwRetVal =
            GetAdaptersAddresses(family, flags, NULL, pAddresses, &outBufLen);

        if (dwRetVal == ERROR_BUFFER_OVERFLOW)
		{
            FREE(pAddresses);
            pAddresses = NULL;
        }
		else
		{
            break;
        }

        Iterations++;

    } while ((dwRetVal == ERROR_BUFFER_OVERFLOW) && (Iterations < MAX_TRIES));

	BOOL bIsFound = FALSE;
	IF_INDEX ndxMin = ULONG_MAX;

    if (dwRetVal == NO_ERROR)
	{
        // If successful, output some information from the data we received
        pCurrAddresses = pAddresses;
        while (pCurrAddresses)
		{
			BOOL bIsMatchIp = FALSE;
			UINT i;
            //printf("\tLength of the IP_ADAPTER_ADDRESS struct: %ld\n", pCurrAddresses->Length);
            //printf("IfIndex (IPv4 interface): %u\n", pCurrAddresses->IfIndex);
            //printf("Adapter name: %s\n", pCurrAddresses->AdapterName);

            pUnicast = pCurrAddresses->FirstUnicastAddress;
            if (pUnicast != NULL)
			{
                for (i = 0; pUnicast != NULL; i++)
				{
					for (struct addrinfo * lst = aiList; lst != NULL;)
					{
						if(
							((struct sockaddr_in *)lst->ai_addr)->sin_addr.S_un.S_addr
							== ((struct sockaddr_in *)pUnicast->Address.lpSockaddr)->sin_addr.S_un.S_addr
							)
						{
							bIsMatchIp = TRUE;
						}
						lst = lst->ai_next;
					}

					if(bIsMatchIp)
					{
						if(0 == i)
						{
							printf("IfIndex (IPv4 interface): %u\n", pCurrAddresses->IfIndex);
						}
						printf("Unicast address: %s\n",
							inet_ntoa((struct in_addr)((struct sockaddr_in *)pUnicast->Address.lpSockaddr)->sin_addr));
					}

                    pUnicast = pUnicast->Next;
				}
                //printf("Number of Unicast Addresses: %d\n", i);
            } //else printf("No Unicast Addresses\n");

            pAnycast = pCurrAddresses->FirstAnycastAddress;
            if (pAnycast) {
                for (i = 0; pAnycast != NULL; i++)
				{
					//printf("\tAnycast Address: %s\n", inet_ntoa((struct in_addr)((struct sockaddr_in *)pAnycast->Address.lpSockaddr)->sin_addr));
                    pAnycast = pAnycast->Next;
				}
                //printf("\tNumber of Anycast Addresses: %d\n", i);
            } //else printf("\tNo Anycast Addresses\n");

            pMulticast = pCurrAddresses->FirstMulticastAddress;
            if (pMulticast) {
                for (i = 0; pMulticast != NULL; i++)
				{
					//printf("\tMulticast Address: %s\n", inet_ntoa((struct in_addr)((struct sockaddr_in *)pMulticast->Address.lpSockaddr)->sin_addr));
                    pMulticast = pMulticast->Next;
				}
                //printf("\tNumber of Multicast Addresses: %d\n", i);
            } //else printf("\tNo Multicast Addresses\n");

            pDnServer = pCurrAddresses->FirstDnsServerAddress;
            if (pDnServer) {
                for (i = 0; pDnServer != NULL; i++)
                    pDnServer = pDnServer->Next;
                //printf("\tNumber of DNS Server Addresses: %d\n", i);
            } //else printf("\tNo DNS Server Addresses\n");

            //printf("\tDNS Suffix: %wS\n", pCurrAddresses->DnsSuffix);
            //printf("\tDescription: %wS\n", pCurrAddresses->Description);
            //printf("\tFriendly name: %wS\n", pCurrAddresses->FriendlyName);

            if (pCurrAddresses->PhysicalAddressLength != 0)
			{
				if(bIsMatchIp && pCurrAddresses->IfIndex < ndxMin && iSize <= pCurrAddresses->PhysicalAddressLength)
				{
					ndxMin = pCurrAddresses->IfIndex;
					memcpy(uchPhysAddr, pCurrAddresses->PhysicalAddress, iSize);
					bIsFound = TRUE;

					printf("Physical address: ");
					for (i = 0; i < (int) pCurrAddresses->PhysicalAddressLength;
						 i++) {
						if (i == (pCurrAddresses->PhysicalAddressLength - 1))
							printf("%.2X\n",
								   (int) pCurrAddresses->PhysicalAddress[i]);
						else
							printf("%.2X:",
								   (int) pCurrAddresses->PhysicalAddress[i]);
					}
				}
            }
            //printf("\tFlags: %ld\n", pCurrAddresses->Flags);
            //printf("\tMtu: %lu\n", pCurrAddresses->Mtu);
            //printf("\tIfType: %ld\n", pCurrAddresses->IfType);
            //printf("\tOperStatus: %ld\n", pCurrAddresses->OperStatus);
            //printf("\tIpv6IfIndex (IPv6 interface): %u\n", pCurrAddresses->Ipv6IfIndex);
            //printf("\tZoneIndices (hex): ");
            //for (i = 0; i < 16; i++)
                //printf("%lx ", pCurrAddresses->ZoneIndices[i]);
            //printf("\n");

            //printf("\tTransmit link speed: %I64u\n", pCurrAddresses->TransmitLinkSpeed);
            //printf("\tReceive link speed: %I64u\n", pCurrAddresses->ReceiveLinkSpeed);

            //pPrefix = pCurrAddresses->FirstPrefix;
            //if (pPrefix) {
                //for (i = 0; pPrefix != NULL; i++)
                    //pPrefix = pPrefix->Next;
                //printf("\tNumber of IP Adapter Prefix entries: %d\n", i);
            //} else printf("\tNumber of IP Adapter Prefix entries: 0\n");

            //printf("\n");

            pCurrAddresses = pCurrAddresses->Next;
        }
    }
	else
	{
        printf("Call to GetAdaptersAddresses failed with error: %d\n", dwRetVal);
        if (dwRetVal == ERROR_NO_DATA)
		{
            printf("\tNo addresses were found for the requested parameters\n");
		}
        else
		{
            if (FormatMessage(FORMAT_MESSAGE_ALLOCATE_BUFFER |
                    FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, 
                    NULL, dwRetVal, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),   
                    // Default language
                    (LPTSTR) & lpMsgBuf, 0, NULL))
			{
                printf("\tError: %s", lpMsgBuf);
                LocalFree(lpMsgBuf);
            }
        }
    }

	if(ERROR_SUCCESS == dwRetVal && !bIsFound)
	{
		dwRetVal = ERROR_NOT_FOUND;
	}

    if (pAddresses)
	{
        FREE(pAddresses);
    }

#if 0
	WSACleanup();
#endif // 0

	return dwRetVal;
}

//
///////////////////////////////////////////////////////////////// End of File.


