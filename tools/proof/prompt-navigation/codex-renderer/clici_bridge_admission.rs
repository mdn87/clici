//! Windows x64 admission ABI. Only the native input thread writes `word`.
//! The child receives a read-only handle over its private stdin after Job assignment.
use std::sync::atomic::{AtomicU64, Ordering};

pub const MAGIC: u64 = 0x324943494c43;

#[repr(C)]
struct Record {
    magic: u64,
    parent: u64,
    incarnation: u64,
    word: AtomicU64,
}

#[cfg(windows)]
mod windows {
    use super::*;
    use std::ffi::c_void;
    use std::os::windows::io::AsRawHandle;
    use std::process::Child;
    type Handle = *mut c_void;

    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn CreateFileMappingW(
            file: Handle,
            attributes: Handle,
            protection: u32,
            high: u32,
            low: u32,
            name: *const u16,
        ) -> Handle;
        fn MapViewOfFile(mapping: Handle, access: u32, high: u32, low: u32, size: usize) -> Handle;
        fn UnmapViewOfFile(view: Handle) -> i32;
        fn CloseHandle(handle: Handle) -> i32;
        fn GetCurrentProcess() -> Handle;
        fn DuplicateHandle(
            source: Handle,
            handle: Handle,
            target: Handle,
            duplicate: *mut Handle,
            access: u32,
            inherit: i32,
            options: u32,
        ) -> i32;
        fn CreateJobObjectW(attributes: Handle, name: *const u16) -> Handle;
        fn SetInformationJobObject(
            job: Handle,
            class: i32,
            information: *const ExtendedLimits,
            length: u32,
        ) -> i32;
        fn AssignProcessToJobObject(job: Handle, process: Handle) -> i32;
    }

    #[repr(C)]
    #[derive(Default)]
    struct BasicLimits {
        process_time: i64,
        job_time: i64,
        flags: u32,
        minimum_working_set: usize,
        maximum_working_set: usize,
        active_processes: u32,
        affinity: usize,
        priority: u32,
        scheduling: u32,
    }
    #[repr(C)]
    #[derive(Default)]
    struct ExtendedLimits {
        basic: BasicLimits,
        io: [u64; 6],
        process_memory: usize,
        job_memory: usize,
        peak_process_memory: usize,
        peak_job_memory: usize,
    }

    pub struct Admission {
        mapping: Handle,
        view: *mut Record,
    }
    // Immutable header and one aligned interprocess atomic; sole writer is native UI.
    unsafe impl Send for Admission {}
    unsafe impl Sync for Admission {}
    impl Admission {
        pub fn new() -> Result<Self, String> {
            if !cfg!(target_arch = "x86_64") {
                return Err("admission requires Windows x64".into());
            }
            unsafe {
                let mapping = CreateFileMappingW(
                    -1isize as Handle,
                    std::ptr::null_mut(),
                    4,
                    0,
                    32,
                    std::ptr::null(),
                );
                if mapping.is_null() {
                    return Err(std::io::Error::last_os_error().to_string());
                }
                let view = MapViewOfFile(mapping, 2, 0, 0, 32).cast::<Record>();
                if view.is_null() {
                    CloseHandle(mapping);
                    return Err(std::io::Error::last_os_error().to_string());
                }
                view.write(Record {
                    magic: MAGIC,
                    parent: u64::from(std::process::id()),
                    incarnation: uuid::Uuid::new_v4().as_u128() as u64,
                    word: AtomicU64::new(0),
                });
                Ok(Self { mapping, view })
            }
        }
        pub fn store(&self, word: u64) {
            unsafe {
                (*self.view).word.store(word, Ordering::Release);
            }
        }
        pub fn load(&self) -> u64 {
            unsafe { (*self.view).word.load(Ordering::Acquire) }
        }
        pub fn handshake(&self, child: &Child) -> Result<String, String> {
            unsafe {
                let mut duplicate = std::ptr::null_mut();
                if DuplicateHandle(
                    GetCurrentProcess(),
                    self.mapping,
                    child.as_raw_handle(),
                    &mut duplicate,
                    4,
                    0,
                    0,
                ) == 0
                {
                    return Err(std::io::Error::last_os_error().to_string());
                }
                Ok(serde_json::json!({"version":2,"mappingHandle":duplicate as usize,"incarnation":(*self.view).incarnation}).to_string())
            }
        }
    }
    impl Drop for Admission {
        fn drop(&mut self) {
            unsafe {
                UnmapViewOfFile(self.view.cast());
                CloseHandle(self.mapping);
            }
        }
    }
    pub struct Job(Handle);
    impl Job {
        pub fn assign(child: &Child) -> Result<Self, String> {
            unsafe {
                let job = Self(CreateJobObjectW(std::ptr::null_mut(), std::ptr::null()));
                let limits = ExtendedLimits {
                    basic: BasicLimits {
                        flags: 0x2000,
                        ..Default::default()
                    },
                    ..Default::default()
                };
                if job.0.is_null()
                    || SetInformationJobObject(
                        job.0,
                        9,
                        &limits,
                        std::mem::size_of::<ExtendedLimits>() as u32,
                    ) == 0
                    || AssignProcessToJobObject(job.0, child.as_raw_handle()) == 0
                {
                    return Err(std::io::Error::last_os_error().to_string());
                }
                Ok(job)
            }
        }
    }
    impl Drop for Job {
        fn drop(&mut self) {
            unsafe {
                CloseHandle(self.0);
            }
        }
    }
}

#[cfg(windows)]
pub use windows::{Admission, Job};

#[cfg(not(windows))]
pub struct Admission(AtomicU64);
#[cfg(not(windows))]
impl Admission {
    pub fn new() -> Result<Self, String> {
        Ok(Self(AtomicU64::new(0)))
    }
    pub fn store(&self, value: u64) {
        self.0.store(value, Ordering::Release);
    }
    pub fn load(&self) -> u64 {
        self.0.load(Ordering::Acquire)
    }
}
