import org.gradle.api.tasks.bundling.AbstractArchiveTask
import org.gradle.api.tasks.bundling.Zip

plugins { java }

repositories { mavenCentral() }

val auraJar = System.getenv("AURA_JAR")?.let(::file)
    ?: error("Set AURA_JAR to the exact Aura Launcher Next Shadow JAR")

dependencies {
    compileOnly(files(auraJar))
    testImplementation(files(auraJar))
    testImplementation(platform("org.junit:junit-bom:5.11.4"))
    testImplementation("org.junit.jupiter:junit-jupiter")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}

tasks.withType<JavaCompile>().configureEach { options.release.set(17) }
tasks.withType<Test>().configureEach { useJUnitPlatform() }
tasks.withType<AbstractArchiveTask>().configureEach {
    isPreserveFileTimestamps = false
    isReproducibleFileOrder = true
}
tasks.jar { archiveBaseName.set("aura-dotnet-runtime-host-plugin") }

val publishDirectory = providers.environmentVariable("AURA_DOTNET_PUBLISH_DIR")
val nativePlatform = providers.environmentVariable("AURA_DOTNET_PLATFORM")

tasks.register<Zip>("packageNpl") {
    dependsOn(tasks.jar)
    archiveFileName.set("dev.hmclce.runtime.dotnet-host-v0.1.0-beta.1.npl")
    destinationDirectory.set(layout.buildDirectory.dir("npl"))
    from("plugin.json")
    into("libs") { from(tasks.jar) }
    into(nativePlatform.map { "native/$it" }) { from(publishDirectory) }
    doFirst {
        val platform = nativePlatform.orNull ?: error("Set AURA_DOTNET_PLATFORM")
        val published = publishDirectory.orNull?.let(::file) ?: error("Set AURA_DOTNET_PUBLISH_DIR")
        require(platform in setOf("windows-x64", "windows-arm64", "linux-x64", "linux-arm64", "macos-x64", "macos-arm64"))
        val expected = if (platform.startsWith("windows-")) "aura-dotnet-runtime-host.exe" else "aura-dotnet-runtime-host"
        require(published.isDirectory) { "Publish directory does not exist: $published" }
        require(published.resolve(expected).isFile) { "Expected process Host named $expected" }
    }
}
