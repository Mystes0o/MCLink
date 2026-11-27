package com.easytier.jni;

// The upstream library exports JNI symbols for this exact class name.
public final class EasyTierJNI {
    static { System.loadLibrary("easytier_android_jni"); }

    private EasyTierJNI() {}

    public static native int runNetworkInstance(String config);
    public static native int setTunFd(String instanceName, int fd);
    public static native int retainNetworkInstance(String[] instanceNames);
    public static native String collectNetworkInfos(int maxLength);
    public static native String getLastError();
}
