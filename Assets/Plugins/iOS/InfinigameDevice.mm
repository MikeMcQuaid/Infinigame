#import <Foundation/Foundation.h>
#import <Security/Security.h>
#include <stdlib.h>
#include <string.h>

extern "C" {
char *InfinigameReadAccount(const char *name)
{
    NSDictionary *query = @{(__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: @"Infinigame", (__bridge id)kSecAttrAccount: [NSString stringWithUTF8String:name],
        (__bridge id)kSecReturnData: @YES};
    CFTypeRef result = NULL;
    if (SecItemCopyMatching((__bridge CFDictionaryRef)query, &result) != errSecSuccess) return NULL;
    NSData *data = CFBridgingRelease(result);
    NSString *text = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    return text ? strdup(text.UTF8String) : NULL;
}
void InfinigameFreeAccount(char *value) { free(value); }
int InfinigameWriteAccount(const char *name, const char *value)
{
    NSDictionary *query = @{(__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: @"Infinigame", (__bridge id)kSecAttrAccount: [NSString stringWithUTF8String:name]};
    NSData *data = [[NSString stringWithUTF8String:value] dataUsingEncoding:NSUTF8StringEncoding];
    OSStatus status = SecItemUpdate((__bridge CFDictionaryRef)query, (__bridge CFDictionaryRef)@{(__bridge id)kSecValueData: data});
    if (status == errSecItemNotFound) {
        NSMutableDictionary *item = [query mutableCopy];
        item[(__bridge id)kSecValueData] = data;
        item[(__bridge id)kSecAttrAccessible] = (__bridge id)kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly;
        status = SecItemAdd((__bridge CFDictionaryRef)item, NULL);
    }
    return status;
}
}
